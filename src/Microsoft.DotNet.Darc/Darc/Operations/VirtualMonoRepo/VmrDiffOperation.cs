// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kusto.Data.Common;
using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using static Microsoft.VisualStudio.Services.Graph.GraphResourceIds.Users;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class VmrDiffOperation(
    VmrDiffOptions options,
    IProcessManager processManager,
    IFileSystem fileSystem,
    IGitRepoFactory gitRepoFactory,
    IVersionDetailsParser versionDetailsParser,
    IVmrPatchHandler patchHandler,
    ISourceMappingParser sourceMappingParser) : Operation
{
    private const string GitDirectory = ".git";
    private readonly static string GitSparseCheckoutFile = Path.Combine(GitDirectory, "info", "sparse-checkout");
    private const string HttpsPrefix = "https://";

    public override async Task<int> ExecuteAsync()
    {
        (DiffRepo repo1, DiffRepo repo2) = await ParseInput();

        NativePath tmpPath = new NativePath(Path.GetTempPath()) / Path.GetRandomFileName();
        try
        {
            fileSystem.CreateDirectory(tmpPath);

            (NativePath tmpProductRepo, NativePath tmpVmrProductRepo, string mapping) = repo1.IsVmr ?
                await PrepareReposAsync(repo2, repo1, tmpPath) :
                await PrepareReposAsync(repo1, repo2, tmpPath);
            
            await AddRemoteAndGenerateDiff(tmpProductRepo, tmpVmrProductRepo, repo2.Branch, await GetDiffFilters(mapping));
        }
        finally
        {
            if (fileSystem.DirectoryExists(tmpPath))
            {
                var gitFiles = Directory.GetDirectories(tmpPath, GitDirectory, SearchOption.AllDirectories)
                    .Select(gitDir => Path.Combine(gitDir, "objects"))
                    .SelectMany(q => Directory.GetFiles(q, "*", SearchOption.AllDirectories));
                foreach (var gitFile in gitFiles)
                {
                    var fileInfo = new FileInfo(gitFile);
                    fileInfo.Attributes = FileAttributes.Normal;
                    fileInfo.IsReadOnly = false;
                }
                fileSystem.DeleteDirectory(tmpPath, true);
            }
        }
        return 0;
    }

    private async Task<(NativePath tmpProductRepo, NativePath tmpVmrProductRepo, string mapping)> PrepareReposAsync(DiffRepo productRepo, DiffRepo vmr, NativePath tmpPath)
    {
        var tmpProductRepo = await PrepareProductRepoAsync(productRepo, tmpPath);
        var mapping = versionDetailsParser.ParseVersionDetailsFile(tmpProductRepo / VersionFiles.VersionDetailsXml).Source?.Mapping
            ?? Path.GetFileName(tmpProductRepo);
        var tmpVmrProductRepo = await PrepareVmrAsync(vmr, tmpPath, mapping);

        return (tmpProductRepo, tmpVmrProductRepo, mapping);
    }

    private async Task<NativePath> PrepareVmrAsync(DiffRepo vmr, NativePath tmpPath, string mapping)
    {
        if (string.IsNullOrEmpty(mapping))
        {
            throw new ArgumentException($"When preparing VMR, mapping can't be null");
        }

        var vmrProductRepo = tmpPath / Guid.NewGuid().ToString();
        if (vmr.IsLocal)
        {
            await CheckoutBranch(vmr);
            CopyDirectory(Path.Combine(vmr.Path, VmrInfo.SourceDirName, mapping), vmrProductRepo, true);
        }
        else
        {
            vmrProductRepo = await PartiallyCloneVmrAsync(tmpPath, vmr, mapping);
        }
        await GitInitRepo(vmrProductRepo, vmr.Branch);

        return vmrProductRepo;
    }

    private async Task<NativePath> PrepareProductRepoAsync(DiffRepo repo, NativePath tmpPath)
    {
        var tmpProductRepo = tmpPath / Path.GetFileName(repo.Path);

        if (!repo.IsLocal)
        {
            await processManager.ExecuteGit(Path.GetDirectoryName(tmpProductRepo)!, [
                "clone",
                "--depth", "1",
                repo.Path,
                "-b", repo.Branch,
                tmpProductRepo
            ]);
        }
        else
        {
            await CheckoutBranch(repo);
            CopyDirectory(repo.Path, tmpProductRepo, true);
        }

        return tmpProductRepo;
    }

    private async Task<(DiffRepo repo1, DiffRepo repo2)> ParseInput()
    {
        DiffRepo repo1, repo2;
        var parts = options.Repositories.Split("..", StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 2 || parts.Length < 1)
        {
            throw new ArgumentException($"Invalid input {options.Repositories}");
        }
        
        if (parts.Length == 1)
        {
            var currentPath = Directory.GetCurrentDirectory();
            var res = await processManager.ExecuteGit(currentPath, "status");
            res.ThrowIfFailed("Current directory is not a git repo");
            repo1 = new DiffRepo(currentPath, string.Empty, true, IsLocalRepoVmr(currentPath));
            repo2 = await ParseRepo(parts[0]);
        }
        else
        {
            repo1 = await ParseRepo(parts[0]);
            repo2 = await ParseRepo(parts[1]);
        }
        await VerifyInput(repo1, repo2);

        return (repo1, repo2);
    }

    private async Task<IReadOnlyCollection<string>> GetDiffFilters(string mapping)
    {
        var gitRepo = gitRepoFactory.CreateClient(DarcLib.Constants.DefaultVmrUri);
        return sourceMappingParser.ParseMappingsFromJson(
            (await gitRepo.GetFileContentsAsync($"{VmrInfo.SourceDirName}/{VmrInfo.SourceMappingsFileName}", DarcLib.Constants.DefaultVmrUri, "main")))
            .Where(m => m.Name == mapping)
            .First().Exclude;
    }

    private async Task CheckoutBranch(DiffRepo repo)
    {
        if (!string.IsNullOrEmpty(repo.Branch))
        {
            var res = await processManager.ExecuteGit(repo.Path, [
                    "checkout",
                    repo.Branch
                ]);
            res.ThrowIfFailed($"Failed to checkout requested branch {repo.Branch} in {repo.Path}");
        }
    }

    private async Task<DiffRepo> ParseRepo(string input)
    {
        var branchIndex = input.LastIndexOf(':');
        if (branchIndex == -1)
        {
            throw new ArgumentException("Invalid input format. Expected format is 'repo:branch'");
        }
        var repo = input.Substring(0, branchIndex);
        var branch = input.Substring(branchIndex + 1);
        bool isLocal = !repo.StartsWith(HttpsPrefix);
        bool isVmr = isLocal ? IsLocalRepoVmr(repo) : await IsRemoteRepoVmr(repo, branch);
        return new DiffRepo(repo, branch, isLocal, isVmr);
    }

    private async Task VerifyInput(DiffRepo repo1, DiffRepo repo2)
    {
        if (repo1.IsVmr == repo2.IsVmr)
        {
            throw new DarcException("One of the repos must be a VMR, and the other one a product repo");
        }

        if (repo1.IsLocal)
        {
            var res = await processManager.ExecuteGit(repo1.Path, "status");
            res.ThrowIfFailed($"{repo1.Path} is not a git repo");
        }
        if (repo2.IsLocal)
        {
            var res = await processManager.ExecuteGit(repo2.Path, "status");
            res.ThrowIfFailed($"{repo2.Path} is not a git repo");
        }
    }
        
    private bool IsLocalRepoVmr(string repoPath) => fileSystem.FileExists(Path.Combine(repoPath, VmrInfo.SourceDirName, VmrInfo.SourceMappingsFileName));
    private async Task<bool> IsRemoteRepoVmr(string uri, string branch)
    {
        var gitRepo = gitRepoFactory.CreateClient(uri);
        try
        {
            await gitRepo.GetFileContentsAsync($"{VmrInfo.SourceDirName}/{VmrInfo.SourceMappingsFileName}", uri, branch);
            return true;
        }
        catch(Exception)
        {
            return false;
        }
    }

    private record DiffRepo(string Path, string Branch, bool IsLocal, bool IsVmr);

    private async Task AddRemoteAndGenerateDiff(string repo1, string repo2, string repo2Branch, IReadOnlyCollection<string> filters)
    {
        string remoteName = Guid.NewGuid().ToString();

        await processManager.ExecuteGit(repo1, [
                "remote", "add", remoteName, repo2
            ]);
        var res = await processManager.ExecuteGit(repo1, [
                "fetch", remoteName,
                "--depth=1"
            ]);
        var sha1 = (await processManager.ExecuteGit(repo1, [
                "rev-parse", "HEAD"
            ])).StandardOutput.Trim();
        var sha2 = (await processManager.ExecuteGit(repo1, [
                "rev-parse", $"{remoteName}/{repo2Branch}"
            ])).StandardOutput.Trim();

        var patches = await patchHandler.CreatePatches(
            options.OutputPath,
            sha1,
            sha2,
            path: null,
            filters,
            relativePaths: false,
            workingDir: new NativePath(repo1),
            applicationPath: null,
            CancellationToken.None);

        if (patches.Count == 1)
        {
            using FileStream fs = new(patches[0].Path, FileMode.Open, FileAccess.Read);
            using StreamReader sr = new(fs);
            string? line;
            while ((line = await sr.ReadLineAsync()) != null)
            {
                Console.WriteLine(line);
            }
        }
        else
        {
            Console.WriteLine("Patch was too big so it had to be split into multiple files");
            if (UxHelpers.PromptForYesNo("Do you want to apply the patches without seeing them?"))
            {
                foreach (var patch in patches)
                {
                    await patchHandler.ApplyPatch(patch, new NativePath(repo1), removePatchAfter: true);
                }
            }
            else
            {
                foreach (var patch in patches)
                {
                    Console.WriteLine($"Patch file: {patch.Path} should be applied to {patch.ApplicationPath}");
                }
            }
        }
    }

    private async Task GitInitRepo(string repoPath, string branch)
    {
        await processManager.ExecuteGit(repoPath, [
                "init",
                $"--initial-branch={branch}"
            ]);
        await processManager.ExecuteGit(repoPath, [
                "add", "--all"
            ]);
        await processManager.ExecuteGit(repoPath, [
                "commit",
                "-m", "Initial commit"
            ]);
    }

    private async Task<NativePath> PartiallyCloneVmrAsync(NativePath path, DiffRepo vmr, string mapping)
    {
        var repoPath = path / "dotnet";
        await processManager.ExecuteGit(path, [
                "clone",
                "--depth", "1",
                "--filter=blob:none",
                "--sparse",
                vmr.Path,
                "-b", vmr.Branch,
                repoPath
            ]);
        await File.WriteAllTextAsync(Path.Combine(repoPath, GitSparseCheckoutFile), $"{VmrInfo.SourceDirName}/{mapping}");
        await processManager.ExecuteGit(repoPath, [
                "sparse-checkout",
                "reapply"
            ]);

        return repoPath / VmrInfo.SourceDirName / mapping;
    }

    static void CopyDirectory(string sourceDir, string destinationDir, bool recursive)
    {
        var dir = new DirectoryInfo(sourceDir);

        if (!dir.Exists)
            throw new DirectoryNotFoundException($"Source directory not found: {dir.FullName}");

        DirectoryInfo[] dirs = dir.GetDirectories();

        Directory.CreateDirectory(destinationDir);

        foreach (FileInfo file in dir.GetFiles())
        {
            string targetFilePath = Path.Combine(destinationDir, file.Name);
            file.CopyTo(targetFilePath);
        }

        if (recursive)
        {
            foreach (DirectoryInfo subDir in dirs)
            {
                string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
                CopyDirectory(subDir.FullName, newDestinationDir, true);
            }
        }
    }
}
