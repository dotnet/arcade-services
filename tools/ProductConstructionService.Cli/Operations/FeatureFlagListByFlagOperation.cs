// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.Extensions.Logging;
using ProductConstructionService.Cli.Options;

namespace ProductConstructionService.Cli.Operations;

internal class FeatureFlagListByFlagOperation : IOperation
{
    private readonly FeatureFlagListByFlagOptions _options;
    private readonly IProductConstructionServiceApi _client;
    private readonly ILogger<FeatureFlagListByFlagOperation> _logger;

    public FeatureFlagListByFlagOperation(
        FeatureFlagListByFlagOptions options,
        IProductConstructionServiceApi client,
        ILogger<FeatureFlagListByFlagOperation> logger)
    {
        _options = options;
        _client = client;
        _logger = logger;
    }

    public async Task<int> RunAsync()
    {
        try
        {
            _logger.LogInformation("Listing subscriptions with feature flag '{FlagName}'", _options.FlagName);

            var response = await _client.FeatureFlags.GetSubscriptionsWithFlagAsync(_options.FlagName);

            if (response.Flags?.Count == 0 || response.Flags == null)
            {
                _logger.LogInformation("No subscriptions found with feature flag '{FlagName}'", _options.FlagName);
                return 0;
            }

            _logger.LogInformation("Subscriptions with feature flag '{FlagName}':", _options.FlagName);

            foreach (var flag in response.Flags.OrderBy(f => f.SubscriptionId))
            {
                _logger.LogInformation("  Subscription: {SubscriptionId}", flag.SubscriptionId);
                _logger.LogInformation("  Value: {Value}", flag.Value);
                
                if (flag.CreatedAt.HasValue)
                {
                    _logger.LogInformation("  Created: {CreatedAt:yyyy-MM-dd HH:mm:ss} UTC", flag.CreatedAt.Value);
                }

            }

            _logger.LogInformation("Total: {Total} subscriptions have this flag set", response.Total);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list subscriptions with feature flag {FlagName}", _options.FlagName);
            _logger.LogError("✗ Error: {Message}", ex.Message);
            return 1;
        }
    }
}