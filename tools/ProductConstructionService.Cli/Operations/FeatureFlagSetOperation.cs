// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;
using ProductConstructionService.Cli.Options;

namespace ProductConstructionService.Cli.Operations;

internal class FeatureFlagSetOperation : IOperation
{
    private readonly FeatureFlagSetOptions _options;
    private readonly IProductConstructionServiceApi _client;
    private readonly ILogger<FeatureFlagSetOperation> _logger;

    public FeatureFlagSetOperation(
        FeatureFlagSetOptions options,
        IProductConstructionServiceApi client,
        ILogger<FeatureFlagSetOperation> logger)
    {
        _options = options;
        _client = client;
        _logger = logger;
    }

    public async Task<int> RunAsync()
    {
        try
        {
            if (!Guid.TryParse(_options.SubscriptionId, out var subscriptionId))
            {
                _logger.LogError("Error: Invalid subscription ID '{SubscriptionId}'. Must be a valid GUID.", _options.SubscriptionId);
                return 1;
            }

            _logger.LogInformation("Setting feature flag {FlagName} = {Value} for subscription {SubscriptionId}", _options.FlagName, _options.Value, subscriptionId);

            var request = new SetFeatureFlagRequest(subscriptionId)
            {
                FlagName = _options.FlagName,
                Value = _options.Value,
                ExpiryDays = _options.ExpiryDays
            };

            var result = await _client.FeatureFlags.SetFeatureFlagAsync(request);

            if (result.Success)
            {
                _logger.LogInformation("✓ Successfully set feature flag '{FlagName}' = '{Value}' for subscription {SubscriptionId}", _options.FlagName, _options.Value, subscriptionId);
                
                return 0;
            }
            else
            {
                _logger.LogError("✗ Failed to set feature flag: {Message}", result.Message);
                return 1;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set feature flag");
            _logger.LogError("✗ Error: {Message}", ex.Message);
            return 1;
        }
    }
}
