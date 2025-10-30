// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Maestro.Common;
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
        [{name}] Removal of the repository from VMR

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

            var filesToStage = new List<string>
            {
                _vmrInfo.SourceManifestPath,
                sourcesPath
            };

            await _localGitClient.StageAsync(_vmrInfo.VmrPath, filesToStage, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            // Regenerate files
            if (codeFlowParameters.TpnTemplatePath != null)
            {
                await _thirdPartyNoticesGenerator.UpdateThirdPartyNotices(codeFlowParameters.TpnTemplatePath);
                await _localGitClient.StageAsync(_vmrInfo.VmrPath, new[] { VmrInfo.ThirdPartyNoticesFileName }, cancellationToken);
            }

            if (codeFlowParameters.GenerateCodeOwners)
            {
                await _codeownersGenerator.UpdateCodeowners(cancellationToken);
            }

            if (codeFlowParameters.GenerateCredScanSuppressions)
            {
                await _credScanSuppressionsGenerator.UpdateCredScanSuppressions(cancellationToken);
            }

            var commitMessage = RemovalCommitMessage.Replace("{name}", mapping.Name);
            await CommitAsync(commitMessage);

            await workBranch.MergeBackAsync(commitMessage);

            _logger.LogInformation("Removal of {repo} finished", mapping.Name);
        }
        catch (Exception)
        {
            _logger.LogWarning(
                InterruptedSyncExceptionMessage,
                workBranch.OriginalBranchName.StartsWith("sync") || workBranch.OriginalBranchName.StartsWith("init") || workBranch.OriginalBranchName.StartsWith("remove") ?
                "the original" : workBranch.OriginalBranchName);
            throw;
        }
    }
}
