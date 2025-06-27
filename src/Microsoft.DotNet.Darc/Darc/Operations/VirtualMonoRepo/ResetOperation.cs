// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
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
            
            try
            {
                var mapping = _dependencyTracker.GetMapping(mappingName);
                _logger.LogInformation("Found mapping '{mapping}' pointing to remote '{remote}'", 
                    mappingName, mapping.DefaultRemote);
            }
            catch (Exception ex)
            {
                _logger.LogError("Mapping '{mapping}' not found: {error}", mappingName, ex.Message);
                return Constants.ErrorCode;
            }

            // Perform the reset by updating to the target SHA
            // The VmrUpdater will handle validation of the SHA and perform the reset
            var codeFlowParameters = new CodeFlowParameters(
                AdditionalRemotes: Array.Empty<AdditionalRemote>(),
                TpnTemplatePath: null,
                GenerateCodeOwners: false,
                GenerateCredScanSuppressions: false,
                DiscardPatches: true,
                ApplyAdditionalMappings: true);

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