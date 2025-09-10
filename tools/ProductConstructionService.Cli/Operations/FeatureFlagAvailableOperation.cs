// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Logging;
using ProductConstructionService.Common;
using ProductConstructionService.Cli.Options;

namespace ProductConstructionService.Cli.Operations;

internal class FeatureFlagAvailableOperation : IOperation
{
    private readonly FeatureFlagAvailableOptions _options;
    private readonly ILogger<FeatureFlagAvailableOperation> _logger;

    public FeatureFlagAvailableOperation(
        FeatureFlagAvailableOptions options,
        ILogger<FeatureFlagAvailableOperation> logger)
    {
        _options = options;
        _logger = logger;
    }

    public Task<int> RunAsync()
    {
        try
        {
            _logger.LogInformation("Listing available feature flags");

            Console.WriteLine("Available Feature Flags:");
            Console.WriteLine();

            var flags = FeatureFlags.AllFlags.Values.OrderBy(f => f.Name);

            foreach (var flag in flags)
            {
                Console.WriteLine($"  {flag.Key}");
                Console.WriteLine($"    Name: {flag.Name}");
                Console.WriteLine($"    Type: {flag.Type}");
                Console.WriteLine($"    Description: {flag.Description}");
                Console.WriteLine();
            }

            Console.WriteLine($"Total: {flags.Count()} available flags");
            Console.WriteLine();
            Console.WriteLine("Example usage:");
            Console.WriteLine($"  pcs feature-flag-set --subscription-id \"12345678-1234-1234-1234-123456789012\" --flag \"{FeatureFlags.EnableRebaseStrategy}\" --value \"true\"");

            return Task.FromResult(0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list available feature flags");
            Console.WriteLine($"✗ Error: {ex.Message}");
            return Task.FromResult(1);
        }
    }
}