// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
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

        var subscriptionDescription = await GetSubscriptionDescriptionAsync(subscriptionId);
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

        Console.WriteLine("All feature flags:");

        var groupedFlags = response.Flags.GroupBy(f => f.SubscriptionId).OrderBy(g => g.Key);

        foreach (var group in groupedFlags)
        {
            var subscriptionDescription = await GetSubscriptionDescriptionAsync(group.Key);
            Console.WriteLine("{0}:", subscriptionDescription);

            foreach (var flag in group.OrderBy(f => f.FlagName))
            {
                Console.WriteLine("  {0}: {1}", flag.FlagName, flag.Value);
            }

            Console.WriteLine("");
        }

        Console.WriteLine("Total: {0} flags across {1} subscriptions", response.Total, groupedFlags.Count());
        return (flowControl: true, value: 0);
    }

    private async Task<string> GetSubscriptionDescriptionAsync(Guid subscriptionId)
    {
        try
        {
            var subscription = await _client.Subscriptions.GetSubscriptionAsync(subscriptionId);
            return GetSubscriptionDescription(subscription);
        }
        catch (Exception)
        {
            // If we can't fetch the subscription, fall back to just the ID
            return subscriptionId.ToString();
        }
    }

    private static string GetSubscriptionDescription(Subscription subscription)
    {
        var channelName = subscription.Channel?.Name ?? "unknown channel";
        return $"{subscription.SourceRepository} ({channelName}) → {subscription.TargetRepository} ({subscription.TargetBranch})";
    }
}
