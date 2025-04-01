// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class VmrDiffOperation(
    VmrDiffOptions options,
    IProcessManager processManager,
    IFileSystem fileSystem,
    IGitRepoFactory gitRepoFactory,
    IVersionDetailsParser versionDetailsParser,
    IVmrPatchHandler patchHandler,
    IRemoteFactory remoteFactory,
    ILocalGitRepoFactory localGitRepoFactory) : Operation
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
            
            await AddRemoteAndGenerateDiffAsync(tmpProductRepo, tmpVmrProductRepo, repo2.Ref, await GetDiffFilters(mapping));
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
            await CheckoutBranchAsync(vmr);
            fileSystem.CopyDirectory(Path.Combine(vmr.Remote, VmrInfo.SourceDirName, mapping), vmrProductRepo, true);
        }
        else
        {
            vmrProductRepo = await PartiallyCloneVmrAsync(tmpPath, vmr, mapping);
        }
        await GitInitRepoAsync(vmrProductRepo, vmr.Ref);

        return vmrProductRepo;
    }

    private async Task<NativePath> PrepareProductRepoAsync(DiffRepo repo, NativePath tmpPath)
    {
        var tmpProductRepo = tmpPath / Path.GetFileName(repo.Remote);

        if (!repo.IsLocal)
        {
            fileSystem.CreateDirectory(tmpProductRepo);
            await processManager.ExecuteGit(tmpProductRepo, "init");
            await processManager.ExecuteGit(tmpProductRepo, [
                "remote", "add", "origin", repo.Remote
            ]);
            await processManager.ExecuteGit(tmpProductRepo, [
                "fetch", "--depth", "1", "origin", repo.Ref
            ]);
            await processManager.ExecuteGit(tmpProductRepo, [
                "checkout", repo.Ref
            ]);
        }
        else
        {
            await CheckoutBranchAsync(repo);
            fileSystem.CopyDirectory(repo.Remote, tmpProductRepo, true);
        }

        return tmpProductRepo;
    }

    private async Task<(DiffRepo repo1, DiffRepo repo2)> ParseInput()
    {
        DiffRepo repo1, repo2;
        var parts = options.Repositories.Split("..", StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 2 || parts.Length < 1)
        {
            throw new ArgumentException($"Invalid input {options.Repositories}. Input should be in the following format:" +
                "remote:branch..remote:branch or remote:branch when called from a git repo");
        }
        
        if (parts.Length == 1)
        {
            var currentRepoPath = processManager.FindGitRoot(Directory.GetCurrentDirectory());
            var branch = await localGitRepoFactory.Create(new NativePath(currentRepoPath)).GetCheckedOutBranchAsync();
            repo1 = new DiffRepo(currentRepoPath, branch, IsLocal: true, await IsRepoVmrAsync(currentRepoPath, branch));
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
        var remote = await remoteFactory.CreateRemoteAsync(DarcLib.Constants.DefaultVmrUri);
        return (await remote.GetSourceMappingsAsync(DarcLib.Constants.DefaultVmrUri, "main"))
            .First(m => m.Name == mapping).Exclude;
    }

    /// <summary>
    /// For a given defaultRemote, gets the defaultRef from the VMR source mappings repo
    /// </summary>
    private async Task<string> GetDefaultVmrRepoRef(string defaultRemote)
    {
        var remote = await remoteFactory.CreateRemoteAsync(DarcLib.Constants.DefaultVmrUri);
        return (await remote.GetSourceMappingsAsync(DarcLib.Constants.DefaultVmrUri, "main"))
            .First(m => m.DefaultRemote == defaultRemote).DefaultRef;
    }

    private async Task CheckoutBranchAsync(DiffRepo repo) =>
        await localGitRepoFactory.Create(new NativePath(repo.Remote)).CheckoutAsync(repo.Ref);

    /// <summary>
    /// Parses a repository string to extract the repository name and branch information.
    /// If no branch is provided, then the current branch is used for local repos,
    /// and or the default for remote repos (main for the VMR, defaultRef for product).
    /// </summary>
    /// <returns>An object containing the repository name, branch, local status, and whether it is a VMR.</returns>
    private async Task<DiffRepo> ParseRepo(string input)
    {
        string repo, branch;
        int searchStartIndex = 0;
        bool isLocal, isVmr;
        if (input.StartsWith(HttpsPrefix))
        {
            searchStartIndex = HttpsPrefix.Length;
            isLocal = false;
        }
        else
        {
            isLocal = true;
            // Case where we pass a windows path, like C:\foo\bar
            if (char.IsLetter(input[0]) && input[1] == ':')
            {
                searchStartIndex = 2;
            }
        }

        var branchIndex = input.IndexOf(':', searchStartIndex);
        if (branchIndex == -1)
        {
            repo = input;
            if (isLocal)
            {
                branch = await localGitRepoFactory.Create(new NativePath(repo)).GetCheckedOutBranchAsync();
                isVmr = await IsRepoVmrAsync(repo, branch);
            }
            else
            {
                isVmr = await IsRepoVmrAsync(repo, "main");
                branch = isVmr ? "main" : await GetDefaultVmrRepoRef(repo);
            }
        }
        else
        {
            repo = input.Substring(0, branchIndex);
            branch = input.Substring(branchIndex + 1);
            isVmr = await IsRepoVmrAsync(repo, branch);
        }

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
            var res = await processManager.ExecuteGit(repo1.Remote, "status");
            res.ThrowIfFailed($"{repo1.Remote} is not a git repo");
        }
        if (repo2.IsLocal)
        {
            var res = await processManager.ExecuteGit(repo2.Remote, "status");
            res.ThrowIfFailed($"{repo2.Remote} is not a git repo");
        }
    }
        
    private async Task<bool> IsRepoVmrAsync(string uri, string branch)
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

    private record DiffRepo(string Remote, string Ref, bool IsLocal, bool IsVmr);

    private async Task AddRemoteAndGenerateDiffAsync(string repo1, string repo2, string repo2Branch, IReadOnlyCollection<string> filters)
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

        string outputPath;
        string? tmpDirectory = null;
        if (string.IsNullOrEmpty(options.OutputPath))
        {
            tmpDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            fileSystem.CreateDirectory(tmpDirectory);
            outputPath = Path.Combine(tmpDirectory, Path.GetRandomFileName());
        }
        else
        {
            outputPath = options.OutputPath;
        }

        var patches = await patchHandler.CreatePatches(
            outputPath,
            sha1,
            sha2,
            path: null,
            filters,
            relativePaths: false,
            workingDir: new NativePath(repo1),
            applicationPath: null,
            CancellationToken.None);

        // If tmpDirectory is not null, it means the output path was not provided, so we just want to
        // print out the whole diff
        if (!string.IsNullOrEmpty(tmpDirectory))
        {
            foreach (var patch in patches)
            {
                using FileStream fs = new(patch.Path, FileMode.Open, FileAccess.Read);
                using StreamReader sr = new(fs);
                string? line;
                while ((line = await sr.ReadLineAsync()) != null)
                {
                    Console.WriteLine(line);
                }
            }
            fileSystem.DeleteDirectory(tmpDirectory, true);
        }
    }

    private async Task GitInitRepoAsync(string repoPath, string branch)
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
        fileSystem.CreateDirectory(repoPath);
        await processManager.ExecuteGit(repoPath, [
                "init"
            ]);
        await processManager.ExecuteGit(repoPath, [
                "remote", "add", "origin", vmr.Remote
            ]);
        // Configure sparse checkup so we don't fetch the whole repo
        await processManager.ExecuteGit(repoPath, [
                "config", "core.sparseCheckout", "true"
            ]);
        fileSystem.WriteToFile(Path.Combine(repoPath, GitSparseCheckoutFile), $"{VmrInfo.SourceDirName}/{mapping}");

        await processManager.ExecuteGit(repoPath, [
                "fetch",
                "--depth" , "1",
                "--filter=blob:none",
                "origin", vmr.Ref
            ]);
        await processManager.ExecuteGit(repoPath, [
                "checkout", vmr.Ref
            ]);

        return repoPath / VmrInfo.SourceDirName / mapping;
    }
}
