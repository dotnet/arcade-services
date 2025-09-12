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
            _logger.LogInformation("Removing feature flag '{FlagName}' from all subscriptions...", _options.FlagName);
            
            // First, let's see how many subscriptions have this flag
            var listResponse = await _client.FeatureFlags.GetSubscriptionsWithFlagAsync(_options.FlagName);
            
            if (listResponse.Flags?.Count == 0 || listResponse.Flags == null)
            {
                _logger.LogInformation("No subscriptions found with feature flag '{FlagName}'", _options.FlagName);
                return 0;
            }

            _logger.LogInformation("Found {Total} subscription(s) with this flag. Proceeding with removal...", listResponse.Total);
            
            var response = await _client.FeatureFlags.RemoveFlagFromAllSubscriptionsAsync(_options.FlagName);

            _logger.LogInformation("✓ {Message}", response.Message);
            
            if (response.RemovedCount > 0)
            {
                _logger.LogInformation("Successfully removed feature flag '{FlagName}' from {RemovedCount} subscription(s)", _options.FlagName, response.RemovedCount);
            }
            else
            {
                _logger.LogInformation("Feature flag '{FlagName}' was not found in any subscriptions", _options.FlagName);
            }

            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove feature flag {FlagName} from all subscriptions", _options.FlagName);
            _logger.LogError("✗ Error: {Message}", ex.Message);
            return 1;
        }
    }
}