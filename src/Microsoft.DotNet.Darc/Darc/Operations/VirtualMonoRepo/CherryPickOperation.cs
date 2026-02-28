// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
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
    private readonly ISourceMappingParser _sourceMappingParser;
    private readonly ILogger<CherryPickOperation> _logger;

    public CherryPickOperation(
        CherryPickCommandLineOptions options,
        IProcessManager processManager,
        IFileSystem fileSystem,
        IVmrPatchHandler patchHandler,
        IVersionDetailsParser versionDetailsParser,
        ISourceMappingParser sourceMappingParser,
        ILogger<CherryPickOperation> logger)
    {
        _options = options;
        _processManager = processManager;
        _fileSystem = fileSystem;
        _patchHandler = patchHandler;
        _versionDetailsParser = versionDetailsParser;
        _sourceMappingParser = sourceMappingParser;
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
        if (string.IsNullOrEmpty(_options.Source))
        {
            _logger.LogError("Source repository path is not specified. Use --source option to specify the source repository.");
            return Constants.ErrorCode;
        }

        // Step 1: Determine VMR path - first check --vmr option (defaults to current dir), then fall back to --source
        var vmrCandidatePath = _options.VmrPath;
        NativePath vmrCandidateGitRoot;
        try
        {
            vmrCandidateGitRoot = new NativePath(_processManager.FindGitRoot(vmrCandidatePath));
        }
        catch (Exception e)
        {
            _logger.LogError("Could not find a git repository at '{path}': {message}", vmrCandidatePath, e.Message);
            return Constants.ErrorCode;
        }

        var sourceManifestAtVmrOption = vmrCandidateGitRoot / VmrInfo.DefaultRelativeSourceManifestPath.Path;
        var isVmrAtVmrOption = _fileSystem.FileExists(sourceManifestAtVmrOption);

        var source = new NativePath(_options.Source);
        NativePath vmrPath;
        NativePath repoPath;
        bool isInVmr;
        NativePath applyTarget;

        if (isVmrAtVmrOption)
        {
            // --vmr (or current dir) is a VMR → cherry-pick from repo (--source) to VMR
            vmrPath = vmrCandidateGitRoot;
            repoPath = source;
            isInVmr = true;
            applyTarget = vmrPath;
        }
        else
        {
            // --vmr is not a VMR; check if --source is a VMR (backward compat: running from repo with --source = VMR)
            NativePath sourceGitRoot;
            try
            {
                sourceGitRoot = new NativePath(_processManager.FindGitRoot(source));
            }
            catch (Exception e)
            {
                _logger.LogError("Could not find a git repository at '{path}': {message}", source, e.Message);
                return Constants.ErrorCode;
            }

            var sourceManifestAtSource = sourceGitRoot / VmrInfo.DefaultRelativeSourceManifestPath.Path;
            var isVmrAtSource = _fileSystem.FileExists(sourceManifestAtSource);

            if (!isVmrAtSource)
            {
                _logger.LogError(
                    "Could not find a VMR at '{vmrPath}' (missing '{sourceManifestPath}'). " +
                    "Run the operation from the VMR directory or specify the VMR path with --vmr.",
                    vmrCandidatePath,
                    sourceManifestAtVmrOption);
                return Constants.ErrorCode;
            }

            // --source is the VMR, current dir is the repo (old behavior)
            vmrPath = sourceGitRoot;
            repoPath = vmrCandidateGitRoot;
            isInVmr = false;
            applyTarget = repoPath;
        }

        if (isInVmr)
        {
            _logger.LogInformation("Cherry-picking {commit} from repository ({repoPath}) -> VMR ({vmrPath})",
                _options.Commit, repoPath, vmrPath);
        }
        else
        {
            _logger.LogInformation("Cherry-picking {commit} from VMR ({vmrPath}) -> repository ({repoPath})",
                _options.Commit, vmrPath, repoPath);
        }

        string mappingName = GetMappingFromRepo(repoPath);

        _logger.LogInformation("Detected mapping name: {mappingName}", mappingName);

        var mappings = await _sourceMappingParser.ParseMappings(vmrPath / VmrInfo.DefaultRelativeSourceMappingsPath);
        var mapping = mappings.FirstOrDefault(m => m.Name.Equals(mappingName, StringComparison.OrdinalIgnoreCase))
            ?? throw new DarcException($"Mapping '{mappingName}' not found in source mappings.");

        List<string> filters =
        [
            .. mapping.Include.Select(VmrPatchHandler.GetInclusionRule),
            .. mapping.Exclude.Select(VmrPatchHandler.GetExclusionRule)
        ];

        var srcPath = VmrInfo.GetRelativeRepoSourcesPath(mapping.Name);
        List<VmrIngestionPatch> patches;

        try
        {
            patches = await _patchHandler.CreatePatches(
                _fileSystem.GetTempFileName(),
                $"{_options.Commit}~1",
                _options.Commit,
                path: null,
                filters,
                relativePaths: true,
                workingDir: isInVmr ? repoPath : vmrPath / srcPath,
                applicationPath: isInVmr ? srcPath : null,
                ignoreLineEndings: true);
        }
        catch (ProcessFailedException e) when (e.Message.Contains("bad revision"))
        {
            _logger.LogError("Commit {commit} not found in {path}", _options.Commit, isInVmr ? repoPath : vmrPath);
            return Constants.ErrorCode;
        }

        try
        {
            var conflicts = await _patchHandler.ApplyPatches(patches, applyTarget, removePatchAfter: true, keepConflicts: true);

            if (conflicts.Count > 0)
            {
                _logger.LogError("Conflicts were detected and changes (including conflicts) staged");
                return Constants.ErrorCode;
            }
        }
        finally
        {
            foreach (var patch in patches)
            {
                try
                {
                    _fileSystem.DeleteFile(patch.Path);
                }
                catch
                {
                }
            }
        }

        _logger.LogInformation("Successfully cherry-picked commit {commit} to repository", _options.Commit);
        return Constants.SuccessCode;
    }

    private string GetMappingFromRepo(NativePath repoPath)
    {
        var versionDetailsPath = repoPath / VersionFiles.VersionDetailsXml;
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
}
