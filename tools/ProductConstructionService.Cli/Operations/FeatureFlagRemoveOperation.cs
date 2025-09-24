// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.Extensions.Logging;
using ProductConstructionService.Cli.Options;

namespace ProductConstructionService.Cli.Operations;

internal class FeatureFlagRemoveOperation : IOperation
{
    private readonly FeatureFlagRemoveOptions _options;
    private readonly IProductConstructionServiceApi _client;
    private readonly ILogger<FeatureFlagRemoveOperation> _logger;

    public FeatureFlagRemoveOperation(
        FeatureFlagRemoveOptions options,
        IProductConstructionServiceApi client,
        ILogger<FeatureFlagRemoveOperation> logger)
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

            _logger.LogInformation("Removing feature flag {FlagName} for subscription {SubscriptionId}", _options.FlagName, subscriptionId);

            var removed = await _client.FeatureFlags.RemoveFeatureFlagAsync(_options.FlagName, subscriptionId);

            if (removed)
            {
                _logger.LogInformation("✓ Successfully removed feature flag '{FlagName}' for subscription {SubscriptionId}", _options.FlagName, subscriptionId);
                return 0;
            }
            else
            {
                _logger.LogInformation("Feature flag '{FlagName}' not found for subscription {SubscriptionId}", _options.FlagName, subscriptionId);
                return 1;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove feature flag");
            _logger.LogError("✗ Error: {Message}", ex.Message);
            return 1;
        }
    }
}
