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
                Console.WriteLine($"Error: Invalid subscription ID '{_options.SubscriptionId}'. Must be a valid GUID.");
                return 1;
            }

            Console.WriteLine($"Removing feature flag {_options.FlagName} for subscription {subscriptionId}");

            var removed = await _client.FeatureFlags.RemoveFeatureFlagAsync(_options.FlagName, subscriptionId);

            if (removed)
            {
                Console.WriteLine($"✓ Successfully removed feature flag '{_options.FlagName}' for subscription {subscriptionId}");
                return 0;
            }
            else
            {
                Console.WriteLine($"Feature flag '{_options.FlagName}' not found for subscription {subscriptionId}");
                return 1;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove feature flag");
            Console.WriteLine($"✗ Error: {ex.Message}");
            return 1;
        }
    }
}
