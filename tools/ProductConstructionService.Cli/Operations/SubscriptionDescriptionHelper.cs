// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;

namespace ProductConstructionService.Cli.Operations;

/// <summary>
/// Helper class for generating human-readable subscription descriptions.
/// </summary>
internal static class SubscriptionDescriptionHelper
{
    /// <summary>
    /// Gets a human-readable description of a subscription by its ID.
    /// If the subscription cannot be fetched, returns just the subscription ID.
    /// </summary>
    public static async Task<string> GetSubscriptionDescriptionAsync(
        IProductConstructionServiceApi client,
        Guid subscriptionId)
    {
        try
        {
            var subscription = await client.Subscriptions.GetSubscriptionAsync(subscriptionId);
            return GetSubscriptionDescription(subscription);
        }
        catch (RestApiException)
        {
            // If we can't fetch the subscription (e.g., it was deleted), fall back to just the ID
            return subscriptionId.ToString();
        }
    }

    /// <summary>
    /// Formats a subscription as a human-readable string.
    /// Format: source repo (channel) → target repo (target branch)
    /// </summary>
    public static string GetSubscriptionDescription(Subscription subscription)
    {
        var channelName = subscription.Channel?.Name ?? "unknown channel";
        return $"{subscription.SourceRepository} ({channelName}) → {subscription.TargetRepository} ({subscription.TargetBranch})";
    }
}
