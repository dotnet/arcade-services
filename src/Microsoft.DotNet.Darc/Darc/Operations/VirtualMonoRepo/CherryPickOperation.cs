// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class CherryPickOperation : Operation
{
    private readonly CherryPickCommandLineOptions _options;
    private readonly IProcessManager _processManager;
    private readonly IFileSystem _fileSystem;
    private readonly IVmrPatchHandler _patchHandler;
    private readonly IVersionDetailsParser _versionDetailsParser;
    private readonly ILocalGitRepoFactory _localGitRepoFactory;
    private readonly IVmrInfo _vmrInfo;
    private readonly ILogger<CherryPickOperation> _logger;

    public CherryPickOperation(
        CherryPickCommandLineOptions options,
        IProcessManager processManager,
        IFileSystem fileSystem,
        IVmrPatchHandler patchHandler,
        IVersionDetailsParser versionDetailsParser,
        ILocalGitRepoFactory localGitRepoFactory,
        IVmrInfo vmrInfo,
        ILogger<CherryPickOperation> logger)
    {
        _options = options;
        _processManager = processManager;
        _fileSystem = fileSystem;
        _patchHandler = patchHandler;
        _versionDetailsParser = versionDetailsParser;
        _localGitRepoFactory = localGitRepoFactory;
        _vmrInfo = vmrInfo;
        _logger = logger;
    }

    public override async Task<int> ExecuteAsync()
    {
        try
        {
            return await ExecuteInternalAsync();
        }
        catch (Exception e)
        {
            _logger.LogError("Cherry-pick operation failed: {message}", e.Message);
            _logger.LogDebug("{exception}", e);
            return Constants.ErrorCode;
        }
    }

    private async Task<int> ExecuteInternalAsync()
    {
        // Step 1: Determine if we're in VMR or repo based on source-manifest.json existence
        var currentDirectory = Environment.CurrentDirectory;
        var gitRoot = _processManager.FindGitRoot(currentDirectory);
        var sourceManifestPath = Path.Combine(gitRoot, VmrInfo.DefaultRelativeSourceManifestPath.Path);
        var isInVmr = _fileSystem.FileExists(sourceManifestPath);

        _logger.LogInformation("Cherry-pick operation starting from {location}", isInVmr ? "VMR" : "repository");

        // Step 2: Get mapping name from Version.Details.xml
        string mappingName;
        if (isInVmr)
        {
            // When in VMR, we need to get mapping from the source repo's Version.Details.xml
            mappingName = GetMappingFromRepo(_options.SourceRepo);
        }
        else
        {
            // When in repo, get mapping from current repo's Version.Details.xml
            mappingName = GetMappingFromRepo(gitRoot);
        }

        _logger.LogInformation("Detected mapping name: {mappingName}", mappingName);

        if (isInVmr)
        {
            // Cherry-pick from VMR to repo
            return await CherryPickFromVmrToRepoAsync(gitRoot, mappingName);
        }
        else
        {
            // Cherry-pick from repo to VMR
            return await CherryPickFromRepoToVmrAsync(gitRoot, mappingName);
        }
    }

    private string GetMappingFromRepo(string repoPath)
    {
        var versionDetailsPath = Path.Combine(repoPath, "eng", "Version.Details.xml");
        if (!_fileSystem.FileExists(versionDetailsPath))
        {
            throw new DarcException($"Version.Details.xml not found at {versionDetailsPath}");
        }

        var versionDetails = _versionDetailsParser.ParseVersionDetailsFile(versionDetailsPath);
        var sourceInfo = versionDetails.Source;
        
        if (sourceInfo?.Mapping == null)
        {
            throw new DarcException("No mapping information found in Version.Details.xml Source section");
        }

        return sourceInfo.Mapping;
    }

    private async Task<int> CherryPickFromVmrToRepoAsync(string vmrPath, string mappingName)
    {
        _logger.LogInformation("Cherry-picking commit {commit} from VMR path src/{mapping} to repository {repo}", 
            _options.Commit, mappingName, _options.SourceRepo);

        // Set up VMR info
        _vmrInfo.VmrPath = new NativePath(vmrPath);
        var mappingPath = _vmrInfo.GetRepoSourcesPath(mappingName);

        if (!_fileSystem.DirectoryExists(mappingPath))
        {
            throw new DarcException($"Mapping directory {mappingPath} not found in VMR");
        }

        // Create a patch from the VMR mapping directory
        var vmrRepo = _localGitRepoFactory.Create(new NativePath(vmrPath));
        var patchName = $"cherry-pick-{_options.Commit}";
        var tmpDir = Path.GetTempPath();
        var patches = await _patchHandler.CreatePatches(
            patchName,
            $"{_options.Commit}~1",
            _options.Commit,
            path: VmrInfo.GetRelativeRepoSourcesPath(mappingName),
            filters: null,
            relativePaths: false,
            workingDir: new NativePath(vmrPath),
            applicationPath: null,
            ignoreLineEndings: true);

        // Apply the patch to the target repository
        var targetRepo = _localGitRepoFactory.Create(new NativePath(_options.SourceRepo));
        foreach (var patch in patches)
        {
            await _patchHandler.ApplyPatch(patch, new NativePath(_options.SourceRepo), removePatchAfter: true);
        }

        _logger.LogInformation("Successfully cherry-picked commit {commit} to repository", _options.Commit);
        return Constants.SuccessCode;
    }

    private async Task<int> CherryPickFromRepoToVmrAsync(string repoPath, string mappingName)
    {
        _logger.LogInformation("Cherry-picking commit {commit} from repository to VMR path src/{mapping}", 
            _options.Commit, mappingName);

        // Parse VMR path from options or find it
        var vmrPath = _options.VmrPath;
        if (string.IsNullOrEmpty(vmrPath))
        {
            throw new DarcException("VMR path must be specified when cherry-picking from repository to VMR");
        }

        _vmrInfo.VmrPath = new NativePath(vmrPath);
        var vmrMappingPath = _vmrInfo.GetRepoSourcesPath(mappingName);

        if (!_fileSystem.DirectoryExists(vmrMappingPath))
        {
            throw new DarcException($"Mapping directory {vmrMappingPath} not found in VMR");
        }

        // Create a patch from the source repository
        var sourceRepo = _localGitRepoFactory.Create(new NativePath(repoPath));
        var patchName = $"cherry-pick-{_options.Commit}";
        var patches = await _patchHandler.CreatePatches(
            patchName,
            $"{_options.Commit}~1",
            _options.Commit,
            path: null,
            filters: null,
            relativePaths: false,
            workingDir: new NativePath(repoPath),
            applicationPath: VmrInfo.GetRelativeRepoSourcesPath(mappingName),
            ignoreLineEndings: true);

        // Apply the patch to the VMR mapping directory
        foreach (var patch in patches)
        {
            await _patchHandler.ApplyPatch(patch, vmrMappingPath, removePatchAfter: true);
        }

        _logger.LogInformation("Successfully cherry-picked commit {commit} to VMR", _options.Commit);
        return Constants.SuccessCode;
    }
}