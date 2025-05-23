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
    ISourceMappingParser sourceMappingParser,
    ILocalGitRepoFactory localGitRepoFactory) : Operation
{
    private const string GitDirectory = ".git";
    private readonly static string GitSparseCheckoutFile = Path.Combine(GitDirectory, "info", "sparse-checkout");
    private const string HttpsPrefix = "https://";

    private record Repo(string Remote, string Ref, bool IsLocal, bool IsVmr);

    public override async Task<int> ExecuteAsync()
    {
        (Repo repo1, Repo repo2) = await ParseInput();

        NativePath tmpPath = new NativePath(Path.GetTempPath()) / Path.GetRandomFileName();
        try
        {
            fileSystem.CreateDirectory(tmpPath);

            (NativePath tmpProductRepo, NativePath tmpVmrProductRepo, string mapping) = repo1.IsVmr ?
                await PrepareReposAsync(repo2, repo1, tmpPath) :
                await PrepareReposAsync(repo1, repo2, tmpPath);

            IReadOnlyCollection<string> exclusionFilters = await GetDiffFilters(tmpVmrProductRepo / ".." / "..", repo1.IsVmr ? repo1.Ref : repo2.Ref, mapping);
            await AddRemoteAndGenerateDiffAsync(tmpProductRepo, tmpVmrProductRepo, repo2.Ref, exclusionFilters);
        }
        finally
        {
            if (fileSystem.DirectoryExists(tmpPath))
            {
                GitFile.MakeGitFilesDeletable(tmpPath);
                fileSystem.DeleteDirectory(tmpPath, true);
            }
        }
        return 0;
    }

    private async Task<(NativePath tmpProductRepo, NativePath tmpVmrProductRepo, string mapping)> PrepareReposAsync(Repo productRepo, Repo vmr, NativePath tmpPath)
    {
        var tmpProductRepo = await InitializeTemporaryProductRepositoryAsync(productRepo, tmpPath);
        var mapping = versionDetailsParser.ParseVersionDetailsFile(tmpProductRepo / VersionFiles.VersionDetailsXml).Source?.Mapping
            ?? Path.GetFileName(tmpProductRepo);
        var tmpVmrProductRepo = await InitializeTemporaryVmrAsync(vmr, tmpPath, mapping);

        return (tmpProductRepo, tmpVmrProductRepo, mapping);
    }

    private async Task<NativePath> InitializeTemporaryVmrAsync(Repo vmr, NativePath tmpPath, string mapping)
    {
        if (string.IsNullOrEmpty(mapping))
        {
            throw new ArgumentException($"Mapping can't be null");
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

    private async Task<NativePath> InitializeTemporaryProductRepositoryAsync(Repo repo, NativePath tmpPath)
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

    private async Task<(Repo repo1, Repo repo2)> ParseInput()
    {
        Repo repo1, repo2;
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
            repo1 = new Repo(currentRepoPath, branch, IsLocal: true, await IsRepoVmrAsync(currentRepoPath, branch));
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

    private async Task<IReadOnlyCollection<string>> GetDiffFilters(NativePath vmrPath, string commit, string mapping)
    {
        var vmr = localGitRepoFactory.Create(vmrPath);
        var sourceMappings = await vmr.GetFileFromGitAsync(VmrInfo.DefaultRelativeSourceMappingsPath, commit)
            ?? throw new FileNotFoundException($"Failed to find {VmrInfo.DefaultRelativeSourceMappingsPath} in {vmrPath} at {commit}");

        return sourceMappingParser.ParseMappingsFromJson(sourceMappings)
            .First(m => m.Name == mapping)
            .Exclude;
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

    private async Task CheckoutBranchAsync(Repo repo) =>
        await localGitRepoFactory.Create(new NativePath(repo.Remote)).CheckoutAsync(repo.Ref);

    /// <summary>
    /// Parses a repository string to extract the repository name and branch information.
    /// If no branch is provided, then the current branch is used for local repos,
    /// and or the default for remote repos (main for the VMR, defaultRef for product).
    /// </summary>
    /// <returns>An object containing the repository name, branch, local status, and whether it is a VMR.</returns>
    private async Task<Repo> ParseRepo(string inputRepo)
    {
        string repo, branch;
        int searchStartIndex = 0;
        bool isLocal, isVmr;
        if (inputRepo.StartsWith(HttpsPrefix))
        {
            searchStartIndex = HttpsPrefix.Length;
            isLocal = false;
        }
        else
        {
            isLocal = true;
            // Case where we pass a windows path, like C:\foo\bar
            if (char.IsLetter(inputRepo[0]) && inputRepo[1] == ':')
            {
                searchStartIndex = 2;
            }
        }

        var branchIndex = inputRepo.IndexOf(':', searchStartIndex);
        if (branchIndex == -1)
        {
            repo = inputRepo;
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
            repo = inputRepo.Substring(0, branchIndex);
            branch = inputRepo.Substring(branchIndex + 1);
            isVmr = await IsRepoVmrAsync(repo, branch);
        }

        return new Repo(repo, branch, isLocal, isVmr);
    }

    private async Task VerifyInput(Repo repo1, Repo repo2)
    {
        if (repo1.IsVmr == repo2.IsVmr)
        {
            throw new ArgumentException("One of the repos must be a VMR, and the other one a product repo");
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
        => await gitRepoFactory.CreateClient(uri).IsRepoVmrAsync(uri, branch);

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
            includeAdditionalMappings: false,
            CancellationToken.None);

        try
        {
            if (options.NameOnly)
            {
                var files = new List<UnixPath>();

                // For name-only mode, we'll print the filenames directly from the git patch summary lines
                foreach (var patch in patches)
                {
                    files.AddRange(await patchHandler.GetPatchedFiles(patch.Path, CancellationToken.None));
                }

                var list = files
                    .Select(f => f.Path)
                    .OrderBy(f => f);

                // If the output path was provided, the list will be stored there
                // Otherwise we want to print it
                if (string.IsNullOrEmpty(options.OutputPath))
                {
                    foreach (var file in list)
                    {
                        Console.WriteLine(file);
                    }
                }
                else
                {
                    await File.WriteAllLinesAsync(outputPath, list);
                }
            }
            else
            {
                // For regular diff mode, we print the full diff content
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
            }
        }
        finally
        {
            if (!string.IsNullOrEmpty(tmpDirectory))
            {
                fileSystem.DeleteDirectory(tmpDirectory, true);
            }
        }
    }

    private async Task GitInitRepoAsync(string repoPath, string branch)
    {
        await processManager.ExecuteGit(repoPath, [
                "init",
                $"--initial-branch={branch}"
            ]);
        await processManager.ExecuteGit(repoPath, [
                // We need --force because some repos have files in them which are .gitignore-ed so if you'd copy their contents, some of the files would not be re-added
                "add", "--all", "--force"
            ]);
        await processManager.ExecuteGit(repoPath, [
                "commit",
                "-m", "Initial commit"
            ]);
    }

    private async Task<NativePath> PartiallyCloneVmrAsync(NativePath path, Repo vmr, string mapping)
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
