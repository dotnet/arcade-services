// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Logging;
using ProductConstructionService.Common;
using ProductConstructionService.Cli.Options;

namespace ProductConstructionService.Cli.Operations;

internal class FeatureFlagSetOperation : IOperation
{
    private readonly FeatureFlagSetOptions _options;
    private readonly IFeatureFlagService _featureFlagService;
    private readonly ILogger<FeatureFlagSetOperation> _logger;

    public FeatureFlagSetOperation(
        FeatureFlagSetOptions options,
        IFeatureFlagService featureFlagService,
        ILogger<FeatureFlagSetOperation> logger)
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

            var expiry = _options.ExpiryDays.HasValue ? (TimeSpan?)TimeSpan.FromDays(_options.ExpiryDays.Value) : null;

            _logger.LogInformation("Setting feature flag {FlagName} = {Value} for subscription {SubscriptionId}",
                _options.FlagName, _options.Value, subscriptionId);

            var result = await _featureFlagService.SetFlagAsync(
                subscriptionId,
                _options.FlagName,
                _options.Value,
                expiry);

            if (result.Success)
            {
                Console.WriteLine($"✓ Successfully set feature flag '{_options.FlagName}' = '{_options.Value}' for subscription {subscriptionId}");
                
                if (result.Flag?.Expiry.HasValue == true)
                {
                    Console.WriteLine($"  Expires: {result.Flag.Expiry.Value:yyyy-MM-dd HH:mm:ss} UTC");
                }
                
                return 0;
            }
            else
            {
                Console.WriteLine($"✗ Failed to set feature flag: {result.Message}");
                return 1;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set feature flag");
            Console.WriteLine($"✗ Error: {ex.Message}");
            return 1;
        }
    }
}