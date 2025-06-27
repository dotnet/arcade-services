// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class ResetOperation : Operation
{
    private readonly ResetCommandLineOptions _options;
    private readonly IVmrUpdater _vmrUpdater;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly ILogger<ResetOperation> _logger;

    public ResetOperation(
        ResetCommandLineOptions options,
        IVmrUpdater vmrUpdater,
        IVmrDependencyTracker dependencyTracker,
        ILogger<ResetOperation> logger)
    {
        _options = options;
        _vmrUpdater = vmrUpdater;
        _dependencyTracker = dependencyTracker;
        _logger = logger;
    }

    public override async Task<int> ExecuteAsync()
    {
        try
        {
            // Parse the mapping:sha parameter
            var parts = _options.MappingAndSha.Split(':', 2);
            if (parts.Length != 2)
            {
                _logger.LogError("Invalid format. Expected [mapping]:[sha] but got: {input}", _options.MappingAndSha);
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

            // Perform the reset by updating to the target SHA
            // This will erase differences and repopulate content to match the target SHA
            var codeFlowParameters = new CodeFlowParameters(
                AdditionalRemotes: [],
                TpnTemplatePath: null,
                GenerateCodeOwners: false,
                GenerateCredScanSuppressions: false,  
                DiscardPatches: true,
                ApplyAdditionalMappings: false);

            _logger.LogInformation("Resetting mapping '{mapping}' by setting dependency to empty commit first", mappingName);

            // Reset the dependency to empty commit to ensure a complete reset
            // This mimics the approach used in VmrForwardFlower.OppositeDirectionFlowAsync
            var currentDependency = _dependencyTracker.GetDependencyVersion(mapping);
            if (currentDependency == null)
            {
                _logger.LogError("Could not find current dependency version for mapping '{mapping}'", mappingName);
                return Constants.ErrorCode;
            }

            // Update dependency to empty commit first to force a complete reset
            _dependencyTracker.UpdateDependencyVersion(new VmrDependencyUpdate(
                mapping,
                mapping.DefaultRemote,
                DarcLib.Constants.EmptyGitObject,
                Parent: null,
                OfficialBuildId: null,
                BarId: null));

            _logger.LogInformation("Synchronizing mapping '{mapping}' from empty commit to SHA '{sha}' (complete reset)", 
                mappingName, targetSha);

            bool success = await _vmrUpdater.UpdateRepository(
                mappingName,
                targetSha,
                updateDependencies: false,
                codeFlowParameters,
                lookUpBuilds: false,
                resetToRemoteWhenCloningRepo: true,
                CancellationToken.None);
            
            if (success)
            {
                _logger.LogInformation("Successfully reset VMR mapping '{mapping}' to SHA '{sha}'", mappingName, targetSha);
                return Constants.SuccessCode;
            }
            else
            {
                _logger.LogInformation("VMR mapping '{mapping}' was already at SHA '{sha}' - no reset needed", mappingName, targetSha);
                return Constants.SuccessCode;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while resetting VMR mapping");
            return Constants.ErrorCode;
        }
    }
}