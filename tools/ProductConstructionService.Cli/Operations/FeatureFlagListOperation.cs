// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.MaestroConfiguration.Client;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.Extensions.Logging;
using ProductConstructionService.Cli.Options;

namespace ProductConstructionService.Cli.Operations;

internal class FeatureFlagListOperation : IOperation
{
    private readonly FeatureFlagListOptions _options;
    private readonly IProductConstructionServiceApi _client;
    private readonly ILogger<FeatureFlagListOperation> _logger;
    private readonly ISubscriptionDescriptionHelper _subscriptionHelper;    

    public FeatureFlagListOperation(
        FeatureFlagListOptions options,
        IProductConstructionServiceApi client,
        ILogger<FeatureFlagListOperation> logger,
        ISubscriptionDescriptionHelper subscriptionHelper)
    {
        _options = options;
        _client = client;
        _logger = logger;
        _subscriptionHelper = subscriptionHelper;
    }

    public async Task<int> RunAsync()
    {
        var path = "C:\\Users\\dkurepa\\source\\test\\lala";
        var configuration = LocalConfigurationRepositoryParser.Parse(path);
        var res = await _client.Ingestion.IngestNamespaceAsync("local", saveChanges: true, configuration.ToPcsClient());
        //await _client.Ingestion.DeleteNamespaceAsync("djuradjTest", true);
        try
        {
            if (!string.IsNullOrEmpty(_options.SubscriptionId))
            {
                (var flowControl, var value) = await ListFlagsForSubscription();
                if (!flowControl)
                {
                    return value;
                }
            }
            else
            {
                (var flowControl, var value) = await ListAllFlags();
                if (!flowControl)
                {
                    return value;
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list feature flags");
            _logger.LogError("✗ Error: {Message}", ex.Message);
            return 1;
        }
    }

    private async Task<(bool flowControl, int value)> ListFlagsForSubscription()
    {
        if (!Guid.TryParse(_options.SubscriptionId, out var subscriptionId))
        {
            _logger.LogError("Error: Invalid subscription ID '{SubscriptionId}'. Must be a valid GUID.", _options.SubscriptionId);
            return (flowControl: false, value: 1);
        }

        var subscriptionDescription = await _subscriptionHelper.GetSubscriptionDescriptionAsync(subscriptionId);
        Console.WriteLine("Listing feature flags for subscription {0}", subscriptionDescription);

        var response = await _client.FeatureFlags.GetFeatureFlagsAsync(subscriptionId);

        if (response.Flags?.Count == 0 || response.Flags == null)
        {
            Console.WriteLine("No feature flags found for subscription {0}", subscriptionDescription);
            return (flowControl: false, value: 0);
        }

        Console.WriteLine("Feature flags for subscription {0}:", subscriptionDescription);
        Console.WriteLine("");

        foreach (var flag in response.Flags)
        {
            Console.WriteLine("  {0}: {1}", flag.FlagName, flag.Value);
        }

        Console.WriteLine("");
        Console.WriteLine("Total: {0} flags", response.Total);
        return (flowControl: true, value: 0);
    }

    private async Task<(bool flowControl, int value)> ListAllFlags()
    {
        Console.WriteLine("Listing all feature flags");

        var response = await _client.FeatureFlags.GetAllFeatureFlagsAsync();

        if (response.Flags?.Count == 0 || response.Flags == null)
        {
            Console.WriteLine("No feature flags found in the system");
            return (flowControl: false, value: 0);
        }

        Console.WriteLine("");

        // Group by flag name and value combination
        var groupedByFlagKeyValue = response.Flags
            .GroupBy(f => (f.FlagName, f.Value))
            .OrderBy(g => g.Key.FlagName)
            .ThenBy(g => g.Key.Value);

        // Prefetch all subscription descriptions in parallel
        var allSubscriptionIds = response.Flags.Select(f => f.SubscriptionId).Distinct();
        await _subscriptionHelper.PrefetchSubscriptionDescriptionsAsync(allSubscriptionIds);

        foreach (var group in groupedByFlagKeyValue)
        {
            Console.WriteLine("{0}: {1}", group.Key.FlagName, group.Key.Value);

            foreach (var flag in group.OrderBy(f => f.SubscriptionId))
            {
                var subscriptionDescription = await _subscriptionHelper.GetSubscriptionDescriptionAsync(flag.SubscriptionId);
                Console.WriteLine("  - {0}", subscriptionDescription);
            }

            Console.WriteLine("");
        }

        var uniqueSubscriptions = response.Flags.Select(f => f.SubscriptionId).Distinct().Count();
        var uniqueCombinations = groupedByFlagKeyValue.Count();
        Console.WriteLine("Total: {0} flags across {1} subscriptions ({2} unique key/value combinations)", 
            response.Total, uniqueSubscriptions, uniqueCombinations);
        return (flowControl: true, value: 0);
    }
}
