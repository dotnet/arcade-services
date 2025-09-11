// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.Extensions.Logging;
using ProductConstructionService.Cli.Options;

namespace ProductConstructionService.Cli.Operations;

internal class FeatureFlagRemoveFromAllOperation : IOperation
{
    private readonly FeatureFlagRemoveFromAllOptions _options;
    private readonly IProductConstructionServiceApi _client;
    private readonly ILogger<FeatureFlagRemoveFromAllOperation> _logger;

    public FeatureFlagRemoveFromAllOperation(
        FeatureFlagRemoveFromAllOptions options,
        IProductConstructionServiceApi client,
        ILogger<FeatureFlagRemoveFromAllOperation> logger)
    {
        _options = options;
        _client = client;
        _logger = logger;
    }

    public async Task<int> RunAsync()
    {
        try
        {
            Console.WriteLine($"Removing feature flag '{_options.FlagName}' from all subscriptions...");
            
            // First, let's see how many subscriptions have this flag
            var listResponse = await _client.FeatureFlags.GetSubscriptionsWithFlagAsync(_options.FlagName);
            
            if (listResponse.Flags?.Count == 0 || listResponse.Flags == null)
            {
                Console.WriteLine($"No subscriptions found with feature flag '{_options.FlagName}'");
                return 0;
            }

            Console.WriteLine($"Found {listResponse.Total} subscription(s) with this flag. Proceeding with removal...");
            
            var response = await _client.FeatureFlags.RemoveFlagFromAllSubscriptionsAsync(_options.FlagName);

            if (response.Success)
            {
                Console.WriteLine($"✓ {response.Message}");
                
                if (response.RemovedCount > 0)
                {
                    Console.WriteLine($"Successfully removed feature flag '{_options.FlagName}' from {response.RemovedCount} subscription(s)");
                }
                else
                {
                    Console.WriteLine($"Feature flag '{_options.FlagName}' was not found in any subscriptions");
                }
            }
            else
            {
                Console.WriteLine($"✗ Failed to remove feature flag: {response.Message}");
                return 1;
            }

            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove feature flag {FlagName} from all subscriptions", _options.FlagName);
            Console.WriteLine($"✗ Error: {ex.Message}");
            return 1;
        }
    }
}