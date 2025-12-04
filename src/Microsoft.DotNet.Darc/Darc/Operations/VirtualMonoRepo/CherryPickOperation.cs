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

        // Step 1: Determine if we're in VMR or repo based on source-manifest.json existence
        var currentDirectory = Environment.CurrentDirectory;
        var gitRoot = new NativePath(_processManager.FindGitRoot(currentDirectory));
        var sourceManifestPath = gitRoot / VmrInfo.DefaultRelativeSourceManifestPath.Path;
        var isInVmr = _fileSystem.FileExists(sourceManifestPath);
        var source = new NativePath(_options.Source);

        var (vmrPath, repoPath) = isInVmr
            ? (gitRoot, source)
            : (source, gitRoot);

        _logger.LogInformation("Cherry-pick operation starting from {location}", isInVmr ? "VMR" : "repository");

        string mappingName = GetMappingFromRepo(repoPath);

        _logger.LogInformation("Detected mapping name: {mappingName}", mappingName);

        var mappings = await _sourceMappingParser.ParseMappings(vmrPath / VmrInfo.DefaultRelativeSourceMappingsPath);
        var mapping = mappings.FirstOrDefault(m => m.Name.Equals(mappingName, StringComparison.OrdinalIgnoreCase))
            ?? throw new DarcException($"Mapping '{mappingName}' not found in source mappings.");

        _logger.LogInformation("Cherry-picking commit {commit}", _options.Commit);

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
            var conflicts = await _patchHandler.ApplyPatches(patches, gitRoot, removePatchAfter: true, keepConflicts: true);

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
