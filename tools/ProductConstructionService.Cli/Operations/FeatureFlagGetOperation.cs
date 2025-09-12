// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.Extensions.Logging;
using ProductConstructionService.Cli.Options;

namespace ProductConstructionService.Cli.Operations;

internal class FeatureFlagGetOperation : IOperation
{
    private readonly FeatureFlagGetOptions _options;
    private readonly IProductConstructionServiceApi _client;
    private readonly ILogger<FeatureFlagGetOperation> _logger;

    public FeatureFlagGetOperation(
        FeatureFlagGetOptions options,
        IProductConstructionServiceApi client,
        ILogger<FeatureFlagGetOperation> logger)
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

            if (!string.IsNullOrEmpty(_options.FlagName))
            {
                _logger.LogInformation("Getting feature flag {FlagName} for subscription {SubscriptionId}", _options.FlagName, subscriptionId);

                var flag = await _client.FeatureFlags.GetFeatureFlagAsync(_options.FlagName, subscriptionId);

                if (flag == null)
                {
                    _logger.LogInformation("Feature flag '{FlagName}' not found for subscription {SubscriptionId}", _options.FlagName, subscriptionId);
                    return 1;
                }

                _logger.LogInformation("Feature flag '{FlagName}':", flag.FlagName);
                _logger.LogInformation("  Subscription: {SubscriptionId}", flag.SubscriptionId);
                _logger.LogInformation("  Value: {Value}", flag.Value);
                
                if (flag.CreatedAt.HasValue)
                {
                    _logger.LogInformation("  Created: {CreatedAt:yyyy-MM-dd HH:mm:ss} UTC", flag.CreatedAt.Value);
                }
                
                return 0;
            }
            else
            {
                _logger.LogInformation("Getting all feature flags for subscription {SubscriptionId}", subscriptionId);

                var response = await _client.FeatureFlags.GetFeatureFlagsAsync(subscriptionId);

                if (response.Flags?.Count == 0 || response.Flags == null)
                {
                    _logger.LogInformation("No feature flags found for subscription {SubscriptionId}", subscriptionId);
                    return 0;
                }

                _logger.LogInformation("Feature flags for subscription {SubscriptionId}:", subscriptionId);

                foreach (var flag in response.Flags)
                {
                    _logger.LogInformation("  {FlagName}: {Value}", flag.FlagName, flag.Value);
                }

                _logger.LogInformation("Total: {Total} flags", response.Total);
                return 0;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get feature flag(s)");
            _logger.LogError("✗ Error: {Message}", ex.Message);
            return 1;
        }
    }
}
