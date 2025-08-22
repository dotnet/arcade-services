// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class VmrDiffOperation : Operation
{
    private const string GitDirectory = ".git";
    private const string HttpsPrefix = "https://";

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
        if (string.IsNullOrEmpty(_options.Repositories))
        {
            // Default behavior: diff against all repositories in source-manifest.json
            return await ExecuteMultiRepoDiffAsync();
        }

        // Check if single argument is a mapping name from VMR context
        var parts = _options.Repositories.Split("..", StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
        {
            string currentRepoPath;
            try
            {
                currentRepoPath = _processManager.FindGitRoot(Directory.GetCurrentDirectory());
            }
            catch (Exception)
            {
                // Not in a git repository, proceed with normal parsing
                goto normalParsing;
            }

            var branch = await _localGitRepoFactory.Create(new NativePath(currentRepoPath)).GetCheckedOutBranchAsync();
            var isCurrentVmr = await IsRepoVmrAsync(currentRepoPath, branch);
            
            if (isCurrentVmr && await IsSingleMappingNameAsync(parts[0]))
            {
                // Single mapping name - diff only that repository
                return await ExecuteSingleMappingDiffAsync(parts[0]);
            }
        }

        normalParsing:

        (Repo repo, Repo vmr, bool fromRepoDirection) = await ParseInput();
        if (_options.NameOnly)
        {
            return await FileTreeDiffAsync(repo, vmr, fromRepoDirection);
        }
        else
        {
            return await FullVmrDiffAsync(repo, vmr);
        }
    }

    private async Task<int> FullVmrDiffAsync(Repo repo, Repo vmr)
    {
        NativePath tmpPath = new NativePath(Path.GetTempPath()) / Path.GetRandomFileName();
        try
        {
            _fileSystem.CreateDirectory(tmpPath);

            (NativePath tmpRepo, NativePath tmpVmr, string mapping) = await PrepareReposAsync(repo, vmr, tmpPath);

            IReadOnlyCollection<string> exclusionFilters = await GetDiffFilters(vmr.Remote, vmr.Ref, mapping);
            return await GenerateDiff(tmpRepo, tmpVmr, vmr.Ref, exclusionFilters);
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

    private async Task<int> ExecuteMultiRepoDiffAsync()
    {
        string currentRepoPath;
        try
        {
            currentRepoPath = _processManager.FindGitRoot(Directory.GetCurrentDirectory());
        }
        catch (Exception)
        {
            throw new ArgumentException("Default diff behavior (no repository specified) can only be used from within a git repository directory");
        }

        var branch = await _localGitRepoFactory.Create(new NativePath(currentRepoPath)).GetCheckedOutBranchAsync();
        var isVmr = await IsRepoVmrAsync(currentRepoPath, branch);
        
        if (!isVmr)
        {
            throw new ArgumentException("Default diff behavior (no repository specified) can only be used from within a VMR directory");
        }

        var vmrRepo = new Repo(currentRepoPath, branch, IsLocal: true, IsVmr: true);
        var sourceManifestPath = new NativePath(currentRepoPath) / VmrInfo.DefaultRelativeSourceManifestPath.Path;
        
        if (!_fileSystem.FileExists(sourceManifestPath))
        {
            throw new FileNotFoundException($"Source manifest not found at {sourceManifestPath}. This directory may not be a valid VMR.");
        }

        var sourceManifestContent = await _fileSystem.ReadAllTextAsync(sourceManifestPath);
        var sourceManifest = SourceManifest.FromJson(sourceManifestContent);

        if (_options.NameOnly)
        {
            return await ExecuteMultiRepoNameOnlyDiffAsync(vmrRepo, sourceManifest);
        }
        else
        {
            return await ExecuteMultiRepoFullDiffAsync(vmrRepo, sourceManifest);
        }
    }

    private async Task<int> ExecuteMultiRepoNameOnlyDiffAsync(Repo vmrRepo, SourceManifest sourceManifest)
    {
        bool hasAnyDifferences = false;
        int totalRepos = sourceManifest.Repositories.Count;
        int currentRepo = 0;

        foreach (var repository in sourceManifest.Repositories)
        {
            currentRepo++;
            var repoArg = $"{repository.RemoteUri}:{repository.CommitSha}";
            var targetRepo = await ParseRepo(repoArg);

            Console.WriteLine($"[{currentRepo}/{totalRepos}] Diffing {repository.Path} / {repository.CommitSha}");
            Console.WriteLine(new string('-', 80));

            try
            {
                int exitCode = await FileTreeDiffAsync(targetRepo, vmrRepo, false);
                if (exitCode == 1)
                {
                    hasAnyDifferences = true;
                }
                else
                {
                    Console.WriteLine("(No differences found)");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: Failed to execute diff for {repository.Path}: {ex.Message}");
                hasAnyDifferences = true;
            }

            Console.WriteLine();
            Console.WriteLine(new string('=', 80));
            Console.WriteLine();
        }

        return hasAnyDifferences ? 1 : 0;
    }

    private async Task<int> ExecuteMultiRepoFullDiffAsync(Repo vmrRepo, SourceManifest sourceManifest)
    {
        bool hasAnyDifferences = false;

        foreach (var repository in sourceManifest.Repositories)
        {
            var repoArg = $"{repository.RemoteUri}:{repository.CommitSha}";
            var targetRepo = await ParseRepo(repoArg);

            try
            {
                int exitCode = await FullVmrDiffAsync(targetRepo, vmrRepo);
                if (exitCode == 1)
                {
                    hasAnyDifferences = true;
                }
            }
            catch
            {
                // In full diff mode, we don't output error messages to keep it silent
                hasAnyDifferences = true;
            }
        }

        return hasAnyDifferences ? 1 : 0;
    }

    private async Task<int> ExecuteSingleMappingDiffAsync(string mappingName)
    {
        var currentRepoPath = _processManager.FindGitRoot(Directory.GetCurrentDirectory());
        var branch = await _localGitRepoFactory.Create(new NativePath(currentRepoPath)).GetCheckedOutBranchAsync();
        var vmrRepo = new Repo(currentRepoPath, branch, IsLocal: true, IsVmr: true);
        
        var sourceManifestPath = new NativePath(currentRepoPath) / VmrInfo.DefaultRelativeSourceManifestPath.Path;
        var sourceManifestContent = await _fileSystem.ReadAllTextAsync(sourceManifestPath);
        var sourceManifest = SourceManifest.FromJson(sourceManifestContent);

        if (!sourceManifest.TryGetRepoVersion(mappingName, out var repoVersion))
        {
            throw new ArgumentException($"No manifest record named {mappingName} found");
        }

        var repoArg = $"{repoVersion.RemoteUri}:{repoVersion.CommitSha}";
        var targetRepo = await ParseRepo(repoArg);

        if (_options.NameOnly)
        {
            Console.WriteLine($"Diffing {repoVersion.Path} / {repoVersion.CommitSha}");
            Console.WriteLine(new string('-', 80));

            try
            {
                int exitCode = await FileTreeDiffAsync(targetRepo, vmrRepo, false);
                if (exitCode == 0)
                {
                    Console.WriteLine("(No differences found)");
                }
                return exitCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: Failed to execute diff for {mappingName}: {ex.Message}");
                return 1;
            }
        }
        else
        {
            return await FullVmrDiffAsync(targetRepo, vmrRepo);
        }
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

    private async Task<(Repo Repo, Repo Vmr, bool fromRepoDirection)> ParseInput()
    {
        if (string.IsNullOrEmpty(_options.Repositories))
        {
            throw new ArgumentException("Repositories parameter should not be null when calling ParseInput directly");
        }

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
            
            // Check if we're in a VMR and the single argument might be a mapping name
            if (repo1.IsVmr && await IsSingleMappingNameAsync(parts[0]))
            {
                return await ParseSingleMappingAsync(repo1, parts[0]);
            }
            
            repo2 = await ParseRepo(parts[0]);
        }
        else
        {
            repo1 = await ParseRepo(parts[0]);
            repo2 = await ParseRepo(parts[1]);
        }

        await VerifyInput(repo1, repo2);

        return repo1.IsVmr ? (repo2, repo1, false) : (repo1, repo2, true);
    }

    private async Task<bool> IsSingleMappingNameAsync(string input)
    {
        // A mapping name is likely a simple name without path separators, URI schemes, or colons
        // If it contains known URI schemes or path separators, it's likely not a mapping name
        if (input.StartsWith("http://") || input.StartsWith("https://") || 
            input.Contains("/") || input.Contains("\\") ||
            (input.Length > 2 && char.IsLetter(input[0]) && input[1] == ':')) // Windows path like C:
        {
            return false;
        }

        // Check if this matches a mapping name in the source manifest
        var currentRepoPath = _processManager.FindGitRoot(Directory.GetCurrentDirectory());
        var sourceManifestPath = new NativePath(currentRepoPath) / VmrInfo.DefaultRelativeSourceManifestPath.Path;
        
        if (!_fileSystem.FileExists(sourceManifestPath))
        {
            return false;
        }

        try
        {
            var sourceManifestContent = await _fileSystem.ReadAllTextAsync(sourceManifestPath);
            var sourceManifest = SourceManifest.FromJson(sourceManifestContent);
            return sourceManifest.TryGetRepoVersion(input, out _);
        }
        catch
        {
            return false;
        }
    }

    private async Task<(Repo Repo, Repo Vmr, bool fromRepoDirection)> ParseSingleMappingAsync(Repo vmrRepo, string mappingName)
    {
        var currentRepoPath = _processManager.FindGitRoot(Directory.GetCurrentDirectory());
        var sourceManifestPath = new NativePath(currentRepoPath) / VmrInfo.DefaultRelativeSourceManifestPath.Path;
        
        var sourceManifestContent = await _fileSystem.ReadAllTextAsync(sourceManifestPath);
        var sourceManifest = SourceManifest.FromJson(sourceManifestContent);

        if (!sourceManifest.TryGetRepoVersion(mappingName, out var repoVersion))
        {
            throw new ArgumentException($"No manifest record named {mappingName} found");
        }

        var repoArg = $"{repoVersion.RemoteUri}:{repoVersion.CommitSha}";
        var targetRepo = await ParseRepo(repoArg);
        
        return (targetRepo, vmrRepo, false);
    }

    private async Task<IReadOnlyCollection<string>> GetDiffFilters(string vmrRemote, string commit, string mapping)
    {
        var vmr = _gitRepoFactory.CreateClient(vmrRemote);
        var sourceMappings = await vmr.GetFileContentsAsync(VmrInfo.DefaultRelativeSourceMappingsPath, vmrRemote, commit)
            ?? throw new FileNotFoundException($"Failed to find {VmrInfo.DefaultRelativeSourceMappingsPath} in {vmrRemote} at {commit}");

        var sourceManifestJson = await vmr.GetFileContentsAsync(VmrInfo.DefaultRelativeSourceManifestPath, vmrRemote, commit)
            ?? throw new FileNotFoundException($"Failed to find {VmrInfo.DefaultRelativeSourceManifestPath} in {vmrRemote} at {commit}");
        var sourceManifest = SourceManifest.FromJson(sourceManifestJson);
        var submodules = sourceManifest.Submodules
            .Where(s => s.Path.StartsWith(mapping + '/', StringComparison.OrdinalIgnoreCase))
            .Select(s => $"{s.Path.Substring(mapping.Length + 1)}");

        return _sourceMappingParser.ParseMappingsFromJson(sourceMappings)
            .First(m => m.Name == mapping)
            .Exclude
            .Concat(submodules)
            .Select(p => VmrPatchHandler.GetExclusionRule(p))
            .ToList();
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

    private async Task<int> GenerateDiff(string repo1Path, string repo2Path, string repo2Branch, IReadOnlyCollection<string> filters)
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
            ignoreLineEndings: true);

        try
        {
            return await OutputDiff(patches);
        }
        finally
        {
            if (!string.IsNullOrEmpty(tmpDirectory))
            {
                _fileSystem.DeleteDirectory(tmpDirectory, true);
            }
        }
    }

    private async Task<int> OutputDiff(List<VmrIngestionPatch> patches)
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

            return list.Any() ? 1 : 0;
        }
        else
        {
            // For regular diff mode, we print the full diff content
            bool hadChanges = false;
            foreach (var patch in patches)
            {
                using FileStream fs = new(patch.Path, FileMode.Open, FileAccess.Read);
                using StreamReader sr = new(fs);
                string? line;
                while ((line = await sr.ReadLineAsync()) != null)
                {
                    Console.WriteLine(line);
                    hadChanges = true;
                }
            }

            return hadChanges ? 1 : 0;
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

    private async Task<int> FileTreeDiffAsync(Repo sourceRepo, Repo vmrRepo, bool fromRepoDirection)
    {
        var sourceGitClient = _gitRepoFactory.CreateClient(sourceRepo.Remote);
        var vmrGitClient = _gitRepoFactory.CreateClient(vmrRepo.Remote);
        var sourceVersionDetails = _versionDetailsParser.ParseVersionDetailsXml(await sourceGitClient.GetFileContentsAsync(VersionFiles.VersionDetailsXml, sourceRepo.Remote, sourceRepo.Ref));
        var sourceMapping = sourceVersionDetails?.Source?.Mapping ??
            throw new DarcException($"Product repo {sourceRepo.Remote} is missing source tag in {VersionFiles.VersionDetailsXml}");

        var exclusionFilters = (await GetExclusionFilters(vmrGitClient, vmrRepo, sourceMapping))
            .Select(filter => new Regex(ConvertGlobToRegexPattern("/" + filter)))
            .ToList();

        Queue<string?> directoriesToProcess = [];
        directoriesToProcess.Enqueue(null);

        Dictionary<string, string> fileDifferences = [];
        string vmrMappingPath = VmrInfo.GetRelativeRepoSourcesPath(sourceMapping);
        var sourceManifest = SourceManifest.FromJson(
            await vmrGitClient.GetFileContentsAsync(VmrInfo.DefaultRelativeSourceManifestPath, vmrRepo.Remote, vmrRepo.Ref));

        string? currentPath;
        while (directoriesToProcess.Count > 0)
        {
            currentPath = directoriesToProcess.Dequeue();

            var repoFiles = await sourceGitClient.LsTreeAsync(sourceRepo.Remote, sourceRepo.Ref, currentPath);
            var vmrFiles = (await vmrGitClient.LsTreeAsync(vmrRepo.Remote, vmrRepo.Ref, $"{vmrMappingPath}{currentPath}"))
                .Select(item => item with { Path = item.Path.Substring(vmrMappingPath.Length) })
                .ToList();

            repoFiles = FilterExcludedFiles(repoFiles, exclusionFilters);
            vmrFiles = FilterExcludedFiles(vmrFiles, exclusionFilters);

            // Blobs with the same content have the same sha, so we need to take that into consideration
            var filesOnlyInVmr = vmrFiles
                .GroupBy(f => f.Sha)
                .ToDictionary(group => group.Key, group => group.ToList());

            ProcessRepoFiles(
                repoFiles,
                vmrFiles,
                directoriesToProcess,
                filesOnlyInVmr,
                sourceManifest.Submodules,
                fileDifferences,
                fromRepoDirection);

            ProcessVmrOnlyFiles(filesOnlyInVmr, fileDifferences, directoriesToProcess, fromRepoDirection);
        }

        foreach (var difference in fileDifferences.Values.OrderBy(v => v))
        {
            Console.WriteLine(difference);
        }

        return fileDifferences.Count > 0 ? 1 : 0;
    }

    private async Task<IReadOnlyCollection<string>> GetExclusionFilters(IGitRepo vmrGitClient, Repo vmr, string mapping)
    {
        var sourceMappingsContent = await vmrGitClient.GetFileContentsAsync(VmrInfo.DefaultRelativeSourceMappingsPath, vmr.Remote, vmr.Ref)
            ?? throw new FileNotFoundException($"Failed to find {VmrInfo.DefaultRelativeSourceMappingsPath} in {vmr.Remote} at {vmr.Ref}");
        var sourceMappings = _sourceMappingParser.ParseMappingsFromJson(sourceMappingsContent);
        var sourceMapping = sourceMappings.FirstOrDefault(m => m.Name == mapping)
            ?? throw new DarcException($"Mapping {mapping} not found in {VmrInfo.DefaultRelativeSourceMappingsPath}");

        return sourceMapping.Exclude;
    }

    private void RecordBlobDiff(
        GitTreeItem sourceFile,
        IReadOnlyList<GitTreeItem> vmrFiles,
        Dictionary<string, string> fileDifferences,
        bool fromRepoDirection)
    {
        var vmrFile = vmrFiles.FirstOrDefault(vmr => vmr.Path == sourceFile.Path);
        if (vmrFile != null)
        {
            fileDifferences[sourceFile.Path] = $"* {sourceFile.Path}";
        }
        else
        {
            fileDifferences[sourceFile.Path] = $"{GetDiffDirection(fromRepoDirection)} {sourceFile.Path}";
        }
    }

    private void ProcessRepoFiles(
        List<GitTreeItem> repoFiles,
        List<GitTreeItem> vmrFiles,
        Queue<string?> directoriesToProcess,
        Dictionary<string, List<GitTreeItem>> filesOnlyInVmr,
        IReadOnlyCollection<ISourceComponent> submodules,
        Dictionary<string, string> fileDifferences,
        bool fromRepoDirection)
    {
        foreach (var sourceFile in repoFiles)
        {
            if (TryFindFileInVmrAndUpdateFilesOnlyInVmr(sourceFile, filesOnlyInVmr))
            {
                continue;
            }

            if (sourceFile.IsCommit())
            {
                HandleSubmodule(sourceFile, submodules, fileDifferences, filesOnlyInVmr, fromRepoDirection);
            }
            else if (sourceFile.IsBlob())
            {
                RecordBlobDiff(sourceFile, vmrFiles, fileDifferences, fromRepoDirection);
            }
            else if (sourceFile.IsTree())
            {
                if (vmrFiles.Any(vmr => vmr.Path == sourceFile.Path))
                {
                    // the folder exists, but the contents of it changed
                    directoriesToProcess.Enqueue(sourceFile.Path);
                }
                else
                {
                    // TODO: It's possible that the folder we're not looking into here has only files that are excluded by the filters,
                    // and wouldn't actually appear in the final diff, but we don't know that because we just say it's missing.
                    fileDifferences[sourceFile.Path] = $"- tree {sourceFile.Path}";
                }
            }
        }
    }

    private void ProcessVmrOnlyFiles(
        Dictionary<string, List<GitTreeItem>> filesOnlyInVmr,
        Dictionary<string, string> fileDifferences,
        Queue<string?> directoriesToProcess,
        bool fromRepoDirection)
    {
        foreach (var missingFilesWithSameSha in filesOnlyInVmr.Values)
        {
            foreach (var missingFile in missingFilesWithSameSha)
            {
                if (fileDifferences.ContainsKey(missingFile.Path) || directoriesToProcess.Any(p => p == missingFile.Path))
                {
                    continue; // Already added to the diff
                }

                var treeMessage = missingFile.IsTree() ? "tree " : string.Empty;
                fileDifferences[missingFile.Path] = $"{GetDiffDirection(!fromRepoDirection)} {treeMessage}{missingFile.Path}";
            }
        }
    }

    private void HandleSubmodule(
        GitTreeItem sourceFile,
        IReadOnlyCollection<ISourceComponent> submodules,
        Dictionary<string, string> fileDifferences,
        Dictionary<string, List<GitTreeItem>> filesOnlyInVmr,
        bool fromRepoDirection)
    {
        // Submodules are a special case where we have to look into VMRs source manifest
        var submodule = submodules.FirstOrDefault(s => s.Path.Contains(sourceFile.Path));
        if (submodule == null)
        {
            fileDifferences[sourceFile.Path] = $"{GetDiffDirection(fromRepoDirection)} submodule {sourceFile.Path}";
        }
        else if (submodule.CommitSha == sourceFile.Sha)
        {
            var shaToRemove = filesOnlyInVmr.Values.First(groups => groups.Any(elem => elem.Path == sourceFile.Path)).First().Sha; ;
            filesOnlyInVmr.Remove(shaToRemove);
        }
        else
        {
            fileDifferences[sourceFile.Path] = $"* submodule {sourceFile.Path}";
        }
    }

    private bool TryFindFileInVmrAndUpdateFilesOnlyInVmr(
        GitTreeItem sourceFile,
        Dictionary<string, List<GitTreeItem>> filesOnlyInVmr)
    {
        if (!filesOnlyInVmr.TryGetValue(sourceFile.Sha, out var vmrGitTreeItems))
        {
            return false;
        }

        // Files can have the same SHA but different paths, so we need to check if the path matches too
        if (!vmrGitTreeItems.Any(vmrFile => vmrFile.Path == sourceFile.Path))
        {
            return false;
        }

        vmrGitTreeItems = [.. vmrGitTreeItems.Where(vmrFile => vmrFile.Path != sourceFile.Path)];
        if (vmrGitTreeItems.Count == 0)
        {
            filesOnlyInVmr.Remove(sourceFile.Sha);
        }
        else
        {
            filesOnlyInVmr[sourceFile.Sha] = vmrGitTreeItems;
        }

        return true;
    }

    private List<GitTreeItem> FilterExcludedFiles(List<GitTreeItem> gitItems, List<Regex> regexes)
        => gitItems.Where(item => !regexes.Any(regex => regex.IsMatch(item.Path))).ToList();

    /// <summary>
    /// Converts a single glob pattern to a regular expression pattern.
    /// </summary>
    /// <param name="globPattern">The glob pattern to convert.</param>
    /// <returns>A regex pattern that matches the same files as the glob pattern.</returns>
    private string ConvertGlobToRegexPattern(string globPattern)
    {
        if (string.IsNullOrWhiteSpace(globPattern))
        {
            throw new ArgumentException("Glob pattern cannot be null or whitespace.", nameof(globPattern));
        }
        
        // Escape regex special characters first
        string regexPattern = Regex.Escape(globPattern);
        
        // Replace the escaped glob special characters with their regex equivalents
        
        // **/ matches any number of directories
        regexPattern = regexPattern.Replace(@"\*\*/", "(?:.*[/])?");
        
        // ** matches any number of characters including directory separators
        regexPattern = regexPattern.Replace(@"\*\*", ".*");
        
        // * matches any number of characters except directory separators
        regexPattern = regexPattern.Replace(@"\*", "[^/]*");
        
        // ? matches a single character except directory separators
        regexPattern = regexPattern.Replace(@"\?", "[^/]");
        
        // Anchor the pattern
        return $"^{regexPattern}$";
    }

    private char GetDiffDirection(bool fromRepoDirection) => fromRepoDirection ? '-' : '+';
}
