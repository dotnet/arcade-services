// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.Extensions.Logging;

namespace ProductConstructionService.Cli.Operations;

internal class FeatureFlagAvailableOperation : IOperation
{
    private readonly IProductConstructionServiceApi _client;
    private readonly ILogger<FeatureFlagAvailableOperation> _logger;

    public FeatureFlagAvailableOperation(
        IProductConstructionServiceApi client,
        ILogger<FeatureFlagAvailableOperation> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<int> RunAsync()
    {
        try
        {
            var response = await _client.FeatureFlags.GetAvailableFeatureFlagsAsync();

            if (response.Flags?.Count == 0 || response.Flags == null)
            {
                Console.WriteLine("No available feature flags found");
                return 1;
            }

            Console.WriteLine("Available Feature Flags:");
            Console.WriteLine();

            foreach (var flag in response.Flags)
            {
                Console.WriteLine(flag);
                Console.WriteLine();
            }

            Console.WriteLine("Example usage:");
            if (response.Flags.Count > 0)
            {
                var firstFlag = response.Flags.First();
                Console.WriteLine($"  pcs feature-flag-set --subscription-id \"12345678-1234-1234-1234-123456789012\" --flag \"{firstFlag}\" --value \"true\"");
            }

            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list available feature flags");
            Console.WriteLine($"✗ Error: {ex.Message}");
            return 1;
        }
    }
}
