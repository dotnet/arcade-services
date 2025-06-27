// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class ResetOperation : Operation
{
    private readonly ResetCommandLineOptions _options;
    private readonly IVmrInfo _vmrInfo;
    private readonly IVmrUpdater _vmrUpdater;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly IProcessManager _processManager;
    private readonly ILogger<ResetOperation> _logger;

    public ResetOperation(
        ResetCommandLineOptions options,
        IVmrInfo vmrInfo,
        IVmrUpdater vmrUpdater,
        IVmrDependencyTracker dependencyTracker,
        IProcessManager processManager,
        ILogger<ResetOperation> logger)
    {
        _options = options;
        _vmrInfo = vmrInfo;
        _vmrUpdater = vmrUpdater;
        _dependencyTracker = dependencyTracker;
        _processManager = processManager;
        _logger = logger;
    }

    public override async Task<int> ExecuteAsync()
    {
        // Parse the mapping:sha parameter
        var parts = _options.Target.Split(':', 2);
        if (parts.Length != 2)
        {
            _logger.LogError("Invalid format. Expected [mapping]:[sha] but got: {input}", _options.Target);
            return Constants.ErrorCode;
        }

        var mappingName = parts[0];
        var targetSha = parts[1];

        if (string.IsNullOrWhiteSpace(mappingName) || string.IsNullOrWhiteSpace(targetSha))
        {
            _logger.LogError("Both mapping name and SHA must be provided. Got mapping: '{mapping}', SHA: '{sha}'",
                mappingName, targetSha);
            return Constants.ErrorCode;
        }

        _logger.LogInformation("Resetting VMR mapping '{mapping}' to SHA '{sha}'", mappingName, targetSha);

        _vmrInfo.VmrPath = new NativePath(_options.VmrPath);

        // Validate that the mapping exists
        await _dependencyTracker.RefreshMetadata();

        SourceMapping mapping;
        try
        {
            mapping = _dependencyTracker.GetMapping(mappingName);
            _logger.LogInformation("Found mapping '{mapping}' pointing to remote '{remote}'",
                mappingName, mapping.DefaultRemote);
        }
        catch (Exception ex)
        {
            _logger.LogError("Mapping '{mapping}' not found: {error}", mappingName, ex.Message);
            return Constants.ErrorCode;
        }

        var currentVersion = _dependencyTracker.GetDependencyVersion(mapping);
        if (currentVersion == null)
        {
            _logger.LogError("Could not find current dependency version for mapping '{mapping}'", mappingName);
            return Constants.ErrorCode;
        }

        // Additional remotes are in the form of [mapping name]:[remote URI]
        IReadOnlyCollection<AdditionalRemote> additionalRemotes = Array.Empty<AdditionalRemote>();
        if (_options.AdditionalRemotes != null)
        {
            additionalRemotes = _options.AdditionalRemotes
                .Select(a => a.Split(':', 2))
                .Select(parts => new AdditionalRemote(parts[0], parts[1]))
                .ToImmutableArray();
        }

        // Perform the reset by updating to the target SHA
        // This will erase differences and repopulate content to match the target SHA
        var codeFlowParameters = new CodeFlowParameters(
            AdditionalRemotes: additionalRemotes,
            TpnTemplatePath: null,
            GenerateCodeOwners: false,
            GenerateCredScanSuppressions: false,
            DiscardPatches: true,
            ApplyAdditionalMappings: false);

        // We will remove everything not-cloaked and replace it with current contents of the source repo
        // When flowing to the VMR, we remove all files but the cloaked files
        List<string> removalFilters =
        [
            .. mapping.Include.Select(VmrPatchHandler.GetInclusionRule),
            .. mapping.Exclude.Select(VmrPatchHandler.GetExclusionRule)
        ];

        try
        {
            // Delete all uncloaked files in the mapping directory
            var targetDir = _vmrInfo.GetRepoSourcesPath(mapping);
            var result = await _processManager.Execute(
                _processManager.GitExecutable,
                ["rm", "-r", "-q", "--", .. removalFilters],
                workingDir: targetDir);

            result.ThrowIfFailed($"Failed to remove files in {targetDir}");

            // Tell the VMR dependency tracker that the repository has been reset
            _dependencyTracker.UpdateDependencyVersion(new VmrDependencyUpdate(
                mapping,
                mapping.DefaultRemote,
                DarcLib.Constants.EmptyGitObject,
                Parent: null,
                OfficialBuildId: null,
                BarId: null));

            await _vmrUpdater.UpdateRepository(
                mappingName,
                targetSha,
                updateDependencies: false,
                codeFlowParameters,
                lookUpBuilds: false,
                resetToRemoteWhenCloningRepo: false,
                CancellationToken.None);

            _logger.LogInformation("Successfully reset {mapping} to {sha}", mappingName, targetSha);
            return Constants.SuccessCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while resetting {mapping}", mappingName);
            return Constants.ErrorCode;
        }
    }
}
