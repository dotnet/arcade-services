// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.Extensions.Logging;
using ProductConstructionService.Cli.Options;

namespace ProductConstructionService.Cli.Operations;

internal class FeatureFlagListOperation : IOperation
{
    private readonly FeatureFlagListOptions _options;
    private readonly IProductConstructionServiceApi _client;
    private readonly ILogger<FeatureFlagListOperation> _logger;

    public FeatureFlagListOperation(
        FeatureFlagListOptions options,
        IProductConstructionServiceApi client,
        ILogger<FeatureFlagListOperation> logger)
    {
        _options = options;
        _client = client;
        _logger = logger;
    }

    public async Task<int> RunAsync()
    {
        try
        {
            if (!string.IsNullOrEmpty(_options.SubscriptionId))
            {
                (var flowControl, var value) = await ListFlagsForSubscription();
                if (!flowControl)
                {
                    return value;
                }
            }
            else
            {
                (var flowControl, var value) = await ListAllFlags();
                if (!flowControl)
                {
                    return value;
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list feature flags");
            _logger.LogError("✗ Error: {Message}", ex.Message);
            return 1;
        }
    }

    private async Task<(bool flowControl, int value)> ListFlagsForSubscription()
    {
        if (!Guid.TryParse(_options.SubscriptionId, out var subscriptionId))
        {
            _logger.LogError("Error: Invalid subscription ID '{SubscriptionId}'. Must be a valid GUID.", _options.SubscriptionId);
            return (flowControl: false, value: 1);
        }

        _logger.LogInformation("Listing feature flags for subscription {SubscriptionId}", subscriptionId);

        var response = await _client.FeatureFlags.GetFeatureFlagsAsync(subscriptionId);

        if (response.Flags?.Count == 0 || response.Flags == null)
        {
            _logger.LogInformation("No feature flags found for subscription {SubscriptionId}", subscriptionId);
            return (flowControl: false, value: 0);
        }

        _logger.LogInformation("Feature flags for subscription {SubscriptionId}:", subscriptionId);
        _logger.LogInformation("");

        foreach (var flag in response.Flags)
        {
            _logger.LogInformation("  {FlagName}: {Value}", flag.FlagName, flag.Value);
        }

        _logger.LogInformation("");
        _logger.LogInformation("Total: {Total} flags", response.Total);
        return (flowControl: true, value: 0);
    }

    private async Task<(bool flowControl, int value)> ListAllFlags()
    {
        _logger.LogInformation("Listing all feature flags");

        var response = await _client.FeatureFlags.GetAllFeatureFlagsAsync();

        if (response.Flags?.Count == 0 || response.Flags == null)
        {
            _logger.LogInformation("No feature flags found in the system");
            return (flowControl: false, value: 0);
        }

        _logger.LogInformation("All feature flags:");
        _logger.LogInformation("");

        var groupedFlags = response.Flags.GroupBy(f => f.SubscriptionId).OrderBy(g => g.Key);

        foreach (var group in groupedFlags)
        {
            _logger.LogInformation("Subscription {SubscriptionId}:", group.Key);

            foreach (var flag in group.OrderBy(f => f.FlagName))
            {
                _logger.LogInformation("  {FlagName}: {Value}", flag.FlagName, flag.Value);
            }

            _logger.LogInformation("");
        }

        _logger.LogInformation("Total: {Total} flags across {GroupCount} subscriptions", response.Total, groupedFlags.Count());
        return (flowControl: true, value: 0);
    }
}
