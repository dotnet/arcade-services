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
            Console.WriteLine($"Listing subscriptions with feature flag '{_options.FlagName}'");

            var response = await _client.FeatureFlags.GetSubscriptionsWithFlagAsync(_options.FlagName);

            if (response.Flags?.Count == 0 || response.Flags == null)
            {
                Console.WriteLine($"No subscriptions found with feature flag '{_options.FlagName}'");
                return 0;
            }

            Console.WriteLine($"Subscriptions with feature flag '{_options.FlagName}':");
            Console.WriteLine();

            foreach (var flag in response.Flags.OrderBy(f => f.SubscriptionId))
            {
                Console.WriteLine($"  Subscription: {flag.SubscriptionId}");
                Console.WriteLine($"  Value: {flag.Value}");
                
                if (flag.CreatedAt.HasValue)
                {
                    Console.WriteLine($"  Created: {flag.CreatedAt.Value:yyyy-MM-dd HH:mm:ss} UTC");
                }

                Console.WriteLine();
            }

            Console.WriteLine($"Total: {response.Total} subscriptions have this flag set");
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list subscriptions with feature flag {FlagName}", _options.FlagName);
            Console.WriteLine($"✗ Error: {ex.Message}");
            return 1;
        }
    }
}