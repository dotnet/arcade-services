// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Logging;
using ProductConstructionService.Common;
using ProductConstructionService.Cli.Options;

namespace ProductConstructionService.Cli.Operations;

internal class FeatureFlagGetOperation : IOperation
{
    private readonly FeatureFlagGetOptions _options;
    private readonly IFeatureFlagService _featureFlagService;
    private readonly ILogger<FeatureFlagGetOperation> _logger;

    public FeatureFlagGetOperation(
        FeatureFlagGetOptions options,
        IFeatureFlagService featureFlagService,
        ILogger<FeatureFlagGetOperation> logger)
    {
        _options = options;
        _featureFlagService = featureFlagService;
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

            if (!string.IsNullOrEmpty(_options.FlagName))
            {
                // Get specific flag
                _logger.LogInformation("Getting feature flag {FlagName} for subscription {SubscriptionId}",
                    _options.FlagName, subscriptionId);

                var flag = await _featureFlagService.GetFlagAsync(subscriptionId, _options.FlagName);

                if (flag == null)
                {
                    Console.WriteLine($"Feature flag '{_options.FlagName}' not found for subscription {subscriptionId}");
                    return 1;
                }

                Console.WriteLine($"Feature flag '{flag.FlagName}':");
                Console.WriteLine($"  Subscription: {flag.SubscriptionId}");
                Console.WriteLine($"  Value: {flag.Value}");
                
                if (flag.Expiry.HasValue)
                {
                    Console.WriteLine($"  Expires: {flag.Expiry.Value:yyyy-MM-dd HH:mm:ss} UTC");
                }
                
                if (flag.CreatedAt.HasValue)
                {
                    Console.WriteLine($"  Created: {flag.CreatedAt.Value:yyyy-MM-dd HH:mm:ss} UTC");
                }
                
                if (flag.UpdatedAt.HasValue)
                {
                    Console.WriteLine($"  Updated: {flag.UpdatedAt.Value:yyyy-MM-dd HH:mm:ss} UTC");
                }
                
                return 0;
            }
            else
            {
                // Get all flags for subscription
                _logger.LogInformation("Getting all feature flags for subscription {SubscriptionId}", subscriptionId);

                var flags = await _featureFlagService.GetFlagsForSubscriptionAsync(subscriptionId);

                if (flags.Count == 0)
                {
                    Console.WriteLine($"No feature flags found for subscription {subscriptionId}");
                    return 0;
                }

                Console.WriteLine($"Feature flags for subscription {subscriptionId}:");
                Console.WriteLine();

                foreach (var flag in flags)
                {
                    Console.WriteLine($"  {flag.FlagName}: {flag.Value}");
                    
                    if (flag.Expiry.HasValue)
                    {
                        Console.WriteLine($"    Expires: {flag.Expiry.Value:yyyy-MM-dd HH:mm:ss} UTC");
                    }
                }

                Console.WriteLine();
                Console.WriteLine($"Total: {flags.Count} flags");
                return 0;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get feature flag(s)");
            Console.WriteLine($"✗ Error: {ex.Message}");
            return 1;
        }
    }
}