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
                // List flags for specific subscription
                if (!Guid.TryParse(_options.SubscriptionId, out var subscriptionId))
                {
                    Console.WriteLine($"Error: Invalid subscription ID '{_options.SubscriptionId}'. Must be a valid GUID.");
                    return 1;
                }

                _logger.LogInformation("Listing feature flags for subscription {SubscriptionId}", subscriptionId);

                var response = await _client.FeatureFlags.GetFeatureFlagsAsync(subscriptionId);

                if (response.Flags?.Count == 0 || response.Flags == null)
                {
                    Console.WriteLine($"No feature flags found for subscription {subscriptionId}");
                    return 0;
                }

                Console.WriteLine($"Feature flags for subscription {subscriptionId}:");
                Console.WriteLine();

                foreach (var flag in response.Flags)
                {
                    Console.WriteLine($"  {flag.FlagName}: {flag.Value}");
                    
                    if (flag.Expiry.HasValue)
                    {
                        var timeRemaining = flag.Expiry.Value - DateTimeOffset.UtcNow;
                        if (timeRemaining.TotalDays > 0)
                        {
                            Console.WriteLine($"    Expires in {timeRemaining.TotalDays:F1} days ({flag.Expiry.Value:yyyy-MM-dd})");
                        }
                        else
                        {
                            Console.WriteLine($"    Expires: {flag.Expiry.Value:yyyy-MM-dd HH:mm:ss} UTC");
                        }
                    }
                }

                Console.WriteLine();
                Console.WriteLine($"Total: {response.Total} flags");
            }
            else
            {
                // List all flags (admin operation)
                _logger.LogInformation("Listing all feature flags");

                var response = await _client.FeatureFlags.GetAllFeatureFlagsAsync();

                if (response.Flags?.Count == 0 || response.Flags == null)
                {
                    Console.WriteLine("No feature flags found in the system");
                    return 0;
                }

                Console.WriteLine("All feature flags:");
                Console.WriteLine();

                var groupedFlags = response.Flags.GroupBy(f => f.SubscriptionId).OrderBy(g => g.Key);

                foreach (var group in groupedFlags)
                {
                    Console.WriteLine($"Subscription {group.Key}:");
                    
                    foreach (var flag in group.OrderBy(f => f.FlagName))
                    {
                        Console.WriteLine($"  {flag.FlagName}: {flag.Value}");
                        
                        if (flag.Expiry.HasValue)
                        {
                            var timeRemaining = flag.Expiry.Value - DateTimeOffset.UtcNow;
                            if (timeRemaining.TotalDays > 0)
                            {
                                Console.WriteLine($"    Expires in {timeRemaining.TotalDays:F1} days");
                            }
                            else
                            {
                                Console.WriteLine($"    Expires: {flag.Expiry.Value:yyyy-MM-dd HH:mm:ss} UTC");
                            }
                        }
                    }
                    
                    Console.WriteLine();
                }

                Console.WriteLine($"Total: {response.Total} flags across {groupedFlags.Count()} subscriptions");
            }

            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list feature flags");
            Console.WriteLine($"✗ Error: {ex.Message}");
            return 1;
        }
    }
}