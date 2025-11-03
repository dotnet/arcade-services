// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

/// <summary>
/// This class is able to remove a repository from the VMR.
/// It removes the sources, updates the source manifest, and optionally regenerates 
/// third-party notices, codeowners, and credential scan suppressions.
/// </summary>
public class VmrRemover : VmrManagerBase, IVmrRemover
{
    private const string RemovalCommitMessage =
        $$"""
        [{0}] Removal of the repository from VMR

        {{Constants.AUTOMATION_COMMIT_TAG}}
        """;

    private readonly IVmrInfo _vmrInfo;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly IWorkBranchFactory _workBranchFactory;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<VmrRemover> _logger;
    private readonly ISourceManifest _sourceManifest;
    private readonly ILocalGitClient _localGitClient;
    private readonly IThirdPartyNoticesGenerator _thirdPartyNoticesGenerator;
    private readonly ICodeownersGenerator _codeownersGenerator;
    private readonly ICredScanSuppressionsGenerator _credScanSuppressionsGenerator;

    public VmrRemover(
            IVmrDependencyTracker dependencyTracker,
            IVmrPatchHandler patchHandler,
            IThirdPartyNoticesGenerator thirdPartyNoticesGenerator,
            ICodeownersGenerator codeownersGenerator,
            ICredScanSuppressionsGenerator credScanSuppressionsGenerator,
            ILocalGitClient localGitClient,
            ILocalGitRepoFactory localGitRepoFactory,
            IWorkBranchFactory workBranchFactory,
            IFileSystem fileSystem,
            ILogger<VmrRemover> logger,
            ILogger<VmrUpdater> baseLogger,
            ISourceManifest sourceManifest,
            IVmrInfo vmrInfo)
        : base(vmrInfo, dependencyTracker, patchHandler, thirdPartyNoticesGenerator, codeownersGenerator, credScanSuppressionsGenerator, localGitClient, localGitRepoFactory, baseLogger)
    {
        _vmrInfo = vmrInfo;
        _dependencyTracker = dependencyTracker;
        _workBranchFactory = workBranchFactory;
        _fileSystem = fileSystem;
        _logger = logger;
        _sourceManifest = sourceManifest;
        _localGitClient = localGitClient;
        _thirdPartyNoticesGenerator = thirdPartyNoticesGenerator;
        _codeownersGenerator = codeownersGenerator;
        _credScanSuppressionsGenerator = credScanSuppressionsGenerator;
    }

    public async Task RemoveRepository(
        string mappingName,
        CodeFlowParameters codeFlowParameters,
        CancellationToken cancellationToken)
    {
        await _dependencyTracker.RefreshMetadataAsync();
        var mapping = _dependencyTracker.GetMapping(mappingName);

        if (_dependencyTracker.GetDependencyVersion(mapping) is null)
        {
            throw new Exception($"Repository {mapping.Name} does not exist in the VMR");
        }

        var workBranchName = $"remove/{mapping.Name}";
        IWorkBranch workBranch = await _workBranchFactory.CreateWorkBranchAsync(GetLocalVmr(), workBranchName);

        try
        {
            _logger.LogInformation("Removing {name} from the VMR..", mapping.Name);

            var sourcesPath = _vmrInfo.GetRepoSourcesPath(mapping);
            if (_fileSystem.DirectoryExists(sourcesPath))
            {
                _logger.LogInformation("Removing source directory {path}", sourcesPath);
                _fileSystem.DeleteDirectory(sourcesPath, recursive: true);
            }
            else
            {
                _logger.LogWarning("Source directory {path} does not exist", sourcesPath);
            }

            // Remove from source manifest
            _sourceManifest.RemoveRepository(mapping.Name);
            _fileSystem.WriteToFile(_vmrInfo.SourceManifestPath, _sourceManifest.ToJson());

            var pathsToStage = new List<string>
            {
                _vmrInfo.SourceManifestPath,
                sourcesPath
            };

            // Remove source mapping
            var sourceMappingsPath = _vmrInfo.VmrPath / VmrInfo.DefaultRelativeSourceMappingsPath;
            if (await RemoveSourceMappingAsync(mapping.Name, sourceMappingsPath, cancellationToken))
            {
                pathsToStage.Add(sourceMappingsPath);
            }

            await _localGitClient.StageAsync(_vmrInfo.VmrPath, pathsToStage, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            // Regenerate files
            if (codeFlowParameters.TpnTemplatePath != null)
            {
                await _thirdPartyNoticesGenerator.UpdateThirdPartyNotices(codeFlowParameters.TpnTemplatePath);
                await _localGitClient.StageAsync(_vmrInfo.VmrPath, [ VmrInfo.ThirdPartyNoticesFileName ], cancellationToken);
            }

            if (codeFlowParameters.GenerateCodeOwners)
            {
                await _codeownersGenerator.UpdateCodeowners(cancellationToken);
            }

            if (codeFlowParameters.GenerateCredScanSuppressions)
            {
                await _credScanSuppressionsGenerator.UpdateCredScanSuppressions(cancellationToken);
            }

            var commitMessage = string.Format(RemovalCommitMessage, mapping.Name);
            await CommitAsync(commitMessage);

            await workBranch.RebaseAsync(cancellationToken);

            _logger.LogInformation("Repo {repo} removed (staged)", mapping.Name);
        }
        catch
        {
            _logger.LogWarning(
                InterruptedSyncExceptionMessage,
                workBranch.OriginalBranchName.StartsWith("remove") ? "the original" : workBranch.OriginalBranchName);
            throw;
        }
    }

    private async Task<bool> RemoveSourceMappingAsync(
        string repoName,
        LocalPath sourceMappingsPath,
        CancellationToken cancellationToken)
    {
        // Read the existing source-mappings.json file
        var json = await _fileSystem.ReadAllTextAsync(sourceMappingsPath);

        var options = new JsonSerializerOptions
        {
            AllowTrailingCommas = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReadCommentHandling = JsonCommentHandling.Skip,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault | JsonIgnoreCondition.WhenWritingNull,
        };

        var sourceMappingFile = JsonSerializer.Deserialize<SourceMappingFile>(json, options)
            ?? throw new Exception($"Failed to deserialize {VmrInfo.SourceMappingsFileName}");

        // Find and remove the mapping
        var mappingToRemove = sourceMappingFile.Mappings.FirstOrDefault(m => m.Name == repoName);
        if (mappingToRemove == null)
        {
            _logger.LogWarning("Source mapping for '{repoName}' not found in {file}", repoName, VmrInfo.SourceMappingsFileName);
            return false;
        }

        sourceMappingFile.Mappings.Remove(mappingToRemove);

        // Write the updated source-mappings.json file
        var updatedJson = JsonSerializer.Serialize(sourceMappingFile, options);
        _fileSystem.WriteToFile(sourceMappingsPath, updatedJson);

        _logger.LogInformation("Removed source mapping for '{repoName}' from {file}",
            repoName, VmrInfo.SourceMappingsFileName);

        return true;
    }
}
