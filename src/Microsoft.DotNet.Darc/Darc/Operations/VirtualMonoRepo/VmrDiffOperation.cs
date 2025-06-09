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
using TreeItem = (string type, string sha, string path);

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class VmrDiffOperation : Operation
{
    private const string GitDirectory = ".git";
    private const string HttpsPrefix = "https://";
    private const string EmptyFileSha = "e69de29bb2d1d6434b8b29ae775ad8c2e48c5391"; // SHA for an empty file in git

    private static readonly string GitSparseCheckoutFile = Path.Combine(GitDirectory, "info", "sparse-checkout");

    private readonly VmrDiffOptions _options;
    private readonly IProcessManager _processManager;
    private readonly IFileSystem _fileSystem;
    private readonly IGitRepoFactory _gitRepoFactory;
    private readonly IVersionDetailsParser _versionDetailsParser;
    private readonly IVmrPatchHandler _patchHandler;
    private readonly IRemoteFactory _remoteFactory;
    private readonly ISourceMappingParser _sourceMappingParser;
    private readonly ILocalGitRepoFactory _localGitRepoFactory;

    public VmrDiffOperation(
        VmrDiffOptions options,
        IProcessManager processManager,
        IFileSystem fileSystem,
        IGitRepoFactory gitRepoFactory,
        IVersionDetailsParser versionDetailsParser,
        IVmrPatchHandler patchHandler,
        IRemoteFactory remoteFactory,
        ISourceMappingParser sourceMappingParser,
        ILocalGitRepoFactory localGitRepoFactory)
    {
        _options = options;
        _processManager = processManager;
        _fileSystem = fileSystem;
        _gitRepoFactory = gitRepoFactory;
        _versionDetailsParser = versionDetailsParser;
        _patchHandler = patchHandler;
        _remoteFactory = remoteFactory;
        _sourceMappingParser = sourceMappingParser;
        _localGitRepoFactory = localGitRepoFactory;
    }

    private record Repo(string Remote, string Ref, bool IsLocal, bool IsVmr);

    public override async Task<int> ExecuteAsync()
    {
        (Repo repo, Repo vmr) = await ParseInput();
        if (_options.NameOnly)
        {
            await CompareSourceTreeWithVmr(repo, vmr);
        }
        else
        {
            NativePath tmpPath = new NativePath(Path.GetTempPath()) / Path.GetRandomFileName();
            try
            {
                _fileSystem.CreateDirectory(tmpPath);

                (NativePath tmpRepo, NativePath tmpVmr, string mapping) = await PrepareReposAsync(repo, vmr, tmpPath);

                IReadOnlyCollection<string> exclusionFilters = await GetDiffFilters(vmr.Remote, vmr.Ref, mapping);
                await GenerateDiff(tmpRepo, tmpVmr, vmr.Ref, exclusionFilters);
            }
            finally
            {
                if (_fileSystem.DirectoryExists(tmpPath))
                {
                    GitFile.MakeGitFilesDeletable(tmpPath);
                    _fileSystem.DeleteDirectory(tmpPath, true);
                }
            }
        }
        return 0;
    }

    private async Task<(NativePath tmpProductRepo, NativePath tmpVmrProductRepo, string mapping)> PrepareReposAsync(Repo productRepo, Repo vmr, NativePath tmpPath)
    {
        var tmpProductRepo = await InitializeTemporaryProductRepositoryAsync(productRepo, tmpPath);
        var mapping = _versionDetailsParser.ParseVersionDetailsFile(tmpProductRepo / VersionFiles.VersionDetailsXml).Source?.Mapping
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
            _fileSystem.CopyDirectory(Path.Combine(vmr.Remote, VmrInfo.SourceDirName, mapping), vmrProductRepo, true);
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
            _fileSystem.CreateDirectory(tmpProductRepo);
            var localGitRepo = _localGitRepoFactory.Create(tmpProductRepo);
            await localGitRepo.ExecuteGitCommand("init");
            var remote = await localGitRepo.AddRemoteIfMissingAsync(repo.Remote);
            await localGitRepo.ExecuteGitCommand("fetch", "--depth", "1", remote, repo.Ref);
            await localGitRepo.CheckoutAsync(repo.Ref);
        }
        else
        {
            await CheckoutBranchAsync(repo);
            _fileSystem.CopyDirectory(repo.Remote, tmpProductRepo, true);
        }

        return tmpProductRepo;
    }

    private async Task<(Repo Repo, Repo Vmr)> ParseInput()
    {
        Repo repo1, repo2;
        var parts = _options.Repositories.Split("..", StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 2 || parts.Length < 1)
        {
            throw new ArgumentException($"Invalid input {_options.Repositories}. Input should be in the following format:" +
                "remote:branch..remote:branch or remote:branch when called from a git repo");
        }

        if (parts.Length == 1)
        {
            var currentRepoPath = _processManager.FindGitRoot(Directory.GetCurrentDirectory());
            var branch = await _localGitRepoFactory.Create(new NativePath(currentRepoPath)).GetCheckedOutBranchAsync();
            repo1 = new Repo(currentRepoPath, branch, IsLocal: true, await IsRepoVmrAsync(currentRepoPath, branch));
            repo2 = await ParseRepo(parts[0]);
        }
        else
        {
            repo1 = await ParseRepo(parts[0]);
            repo2 = await ParseRepo(parts[1]);
        }

        await VerifyInput(repo1, repo2);

        return repo1.IsVmr ? (repo2, repo1) : (repo1, repo2);
    }

    private async Task<IReadOnlyCollection<string>> GetDiffFilters(string vmrRemote, string commit, string mapping)
    {
        var vmr = _gitRepoFactory.CreateClient(vmrRemote);
        var sourceMappings = await vmr.GetFileContentsAsync(VmrInfo.DefaultRelativeSourceMappingsPath, vmrRemote, commit)
            ?? throw new FileNotFoundException($"Failed to find {VmrInfo.DefaultRelativeSourceMappingsPath} in {vmrRemote} at {commit}");

        return _sourceMappingParser.ParseMappingsFromJson(sourceMappings)
            .First(m => m.Name == mapping)
            .Exclude;
    }

    /// <summary>
    /// For a given defaultRemote, gets the defaultRef from the VMR source mappings repo
    /// </summary>
    private async Task<string> GetDefaultVmrRepoRef(string defaultRemote)
    {
        var remote = await _remoteFactory.CreateRemoteAsync(DarcLib.Constants.DefaultVmrUri);
        return (await remote.GetSourceMappingsAsync(DarcLib.Constants.DefaultVmrUri, "main"))
            .First(m => m.DefaultRemote == defaultRemote).DefaultRef;
    }

    private async Task CheckoutBranchAsync(Repo repo) =>
        await _localGitRepoFactory.Create(new NativePath(repo.Remote)).CheckoutAsync(repo.Ref);

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
                branch = await _localGitRepoFactory.Create(new NativePath(repo)).GetCheckedOutBranchAsync();
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
            var res = await _processManager.ExecuteGit(repo1.Remote, "status");
            res.ThrowIfFailed($"{repo1.Remote} is not a git repo");
        }
        if (repo2.IsLocal)
        {
            var res = await _processManager.ExecuteGit(repo2.Remote, "status");
            res.ThrowIfFailed($"{repo2.Remote} is not a git repo");
        }
    }

    private async Task<bool> IsRepoVmrAsync(string uri, string branch)
        => await _gitRepoFactory.CreateClient(uri).IsRepoVmrAsync(uri, branch);

    private async Task GenerateDiff(string repo1Path, string repo2Path, string repo2Branch, IReadOnlyCollection<string> filters)
    {
        var repo1 = _localGitRepoFactory.Create(new NativePath(repo1Path));
        string remoteName = await repo1.AddRemoteIfMissingAsync(repo2Path);
        await repo1.ExecuteGitCommand("fetch", remoteName, "--depth=1");

        var sha1 = await repo1.GetShaForRefAsync("HEAD");
        var sha2 = await repo1.GetShaForRefAsync($"{remoteName}/{repo2Branch}");

        string outputPath;
        string? tmpDirectory = null;
        if (string.IsNullOrEmpty(_options.OutputPath))
        {
            tmpDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            _fileSystem.CreateDirectory(tmpDirectory);
            outputPath = Path.Combine(tmpDirectory, Path.GetRandomFileName());
        }
        else
        {
            outputPath = _options.OutputPath;
        }

        var patches = await _patchHandler.CreatePatches(
            outputPath,
            sha1,
            sha2,
            path: null,
            filters,
            relativePaths: false,
            workingDir: repo1.Path,
            applicationPath: null,
            includeAdditionalMappings: false,
            CancellationToken.None,
            ignoreLineEndings: true);

        try
        {
            await OutputDiff(patches);
        }
        finally
        {
            if (!string.IsNullOrEmpty(tmpDirectory))
            {
                _fileSystem.DeleteDirectory(tmpDirectory, true);
            }
        }
    }

    private async Task OutputDiff(List<VmrIngestionPatch> patches)
    {
        if (_options.NameOnly)
        {
            var files = new List<UnixPath>();

            // For name-only mode, we'll print the filenames directly from the git patch summary lines
            foreach (var patch in patches)
            {
                files.AddRange(await _patchHandler.GetPatchedFiles(patch.Path, CancellationToken.None));
            }

            var list = files
                .Select(f => f.Path)
                .OrderBy(f => f);

            // If the output path was provided, the list will be stored there
            // Otherwise we want to print it
            if (string.IsNullOrEmpty(_options.OutputPath))
            {
                foreach (var file in list)
                {
                    Console.WriteLine(file);
                }
            }
            else
            {
                await File.WriteAllLinesAsync(_options.OutputPath, list);
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

    private async Task GitInitRepoAsync(NativePath repoPath, string branch)
    {
        var repo = _localGitRepoFactory.Create(repoPath);
        await repo.ExecuteGitCommand("init", $"--initial-branch={branch}");
        // We need --force because some repos have files in them which are .gitignore-ed so if you'd copy their contents, some of the files would not be re-added
        await repo.ExecuteGitCommand("add", "--all", "--force");
        await repo.CommitAsync("Initial commit", allowEmpty: false);
    }

    private async Task<NativePath> PartiallyCloneVmrAsync(NativePath path, Repo vmr, string mapping)
    {
        var repoPath = path / "dotnet";
        _fileSystem.CreateDirectory(repoPath);
        var repo = _localGitRepoFactory.Create(repoPath);

        await repo.ExecuteGitCommand("init");
        var remote = await repo.AddRemoteIfMissingAsync(vmr.Remote);

        // Configure sparse checkup so we don't fetch the whole repo
        await repo.SetConfigValue("core.sparseCheckout", "true");

        _fileSystem.WriteToFile(repoPath / GitSparseCheckoutFile, $"{VmrInfo.SourceDirName}/{mapping}");

        await repo.ExecuteGitCommand(
            "fetch",
            "--depth", "1",
            "--filter=blob:none",
            remote, vmr.Ref);
        await repo.CheckoutAsync(vmr.Ref);

        return repoPath / VmrInfo.SourceDirName / mapping;
    }

    private async Task CompareSourceTreeWithVmr(Repo sourceRepo, Repo vmrRepo)
    {
        var sourceGitClient = _gitRepoFactory.CreateClient(sourceRepo.Remote);
        var vmrGitClient = _gitRepoFactory.CreateClient(vmrRepo.Remote);
        var sourceVersionDetails = _versionDetailsParser.ParseVersionDetailsXml(await sourceGitClient.GetFileContentsAsync(VersionFiles.VersionDetailsXml, sourceRepo.Remote, sourceRepo.Ref));
        var sourceMapping = sourceVersionDetails?.Source?.Mapping ??
            throw new DarcException($"Product repo {sourceRepo.Remote} is missing source tag in {VersionFiles.VersionDetailsXml}");

        Queue<string?> directoriesToProcess = [];
        directoriesToProcess.Enqueue(null);

        Dictionary<string, string> fileDifferences = [];

        string? currentPath;
        while (directoriesToProcess.Count > 0)
        {
            currentPath = directoriesToProcess.Dequeue();
            string vmrMappingPath = $"{VmrInfo.SourcesDir}/{sourceMapping}";

            var sourceFiles = await sourceGitClient.LsTree(sourceRepo.Remote, sourceRepo.Ref, currentPath);
            var vmrFiles = (await vmrGitClient.LsTree(vmrRepo.Remote, vmrRepo.Ref, $"{vmrMappingPath}{currentPath}"))
                .Select(entry => (entry.type, entry.sha, path: entry.path.Substring(vmrMappingPath.Length)))
                .ToList();

            // Handle empty files separately since they all have the same sha
            (sourceFiles, vmrFiles) = CheckAndFilterEmptyFiles(sourceFiles, vmrFiles, fileDifferences);

            var filesOnlyInVmr = vmrFiles.ToDictionary(file => file.sha, file => file);

            foreach (var sourceFile in sourceFiles)
            {
                if (vmrFiles.Any(vmrFile => vmrFile.sha == sourceFile.sha))
                {
                    filesOnlyInVmr.Remove(sourceFile.sha);
                    continue;
                }

                if (IsBlob(sourceFile.type))
                {
                    if (vmrFiles.Any(vmrFile => vmrFile.path == sourceFile.path))
                    {
                        fileDifferences[sourceFile.path] = ($"File {sourceFile.path} has different content in the VMR and in the repo");
                    }
                    else
                    {
                        fileDifferences[sourceFile.path] = ($"File {sourceFile.path} is missing in the VMR but exists in the repo");
                    }
                }
                else if (IsTree(sourceFile.type))
                {
                    directoriesToProcess.Enqueue(sourceFile.path);
                }
            }

            foreach (var missingFile in filesOnlyInVmr.Values)
            {
                if (fileDifferences.ContainsKey(missingFile.path) || directoriesToProcess.Any(p => p == missingFile.path))
                {
                    continue; // Already added to the diff
                }
                if (IsBlob(missingFile.type))
                {
                    fileDifferences[missingFile.path] = ($"File {missingFile.path} is missing in the repo but exists in the VMR"); 
                }
                else if (IsTree(missingFile.type))
                {
                    directoriesToProcess.Enqueue(missingFile.path);
                }
            }
        }

        if (fileDifferences.Count == 0)
        {
            Console.WriteLine("No differences found between the product repo and the VMR.");
            return;
        }
        foreach (var difference in fileDifferences.Values)
        {
            Console.WriteLine(difference);
        }
    }

    bool IsBlob(string type) => type.Equals("Blob", StringComparison.OrdinalIgnoreCase);
    bool IsTree(string type) => type.Equals("Tree", StringComparison.OrdinalIgnoreCase);

    (List<TreeItem>, List<TreeItem>) CheckAndFilterEmptyFiles(List<TreeItem> sourceRepoFiles, List<TreeItem> vmrFiles, Dictionary<string, string> diffResults)
    {
        var sourceEmptyFiles = sourceRepoFiles.Where(file => IsBlob(file.type) && file.sha == EmptyFileSha).ToList();
        var vmrEmptyFiles = vmrFiles.Where(file => IsBlob(file.type) && file.sha == EmptyFileSha).ToList();
        HashSet<string> emptyFilesOnlyInVmr = new(vmrEmptyFiles.Select(file => file.path));
        
        foreach (var sourceEmptyFile in sourceEmptyFiles)
        {
            if (!vmrEmptyFiles.Any(vmrFile => vmrFile.path == sourceEmptyFile.path))
            {
                diffResults[sourceEmptyFile.path] = ($"File {sourceEmptyFile.path} is missing in the VMR but exists in the repo.");
            }
            else
            {
                emptyFilesOnlyInVmr.Remove(sourceEmptyFile.path); // Remove from the set if it exists in both
            }
        }

        foreach (var missingFilePath in emptyFilesOnlyInVmr)
        {
            diffResults[missingFilePath] = ($"File {missingFilePath} is missing in the repo but exists in the VMR.");
        }

        return (
            [.. sourceRepoFiles.Where(file => file.sha != EmptyFileSha)],
            [.. vmrFiles.Where(file => file.sha != EmptyFileSha)]);
    }
}
