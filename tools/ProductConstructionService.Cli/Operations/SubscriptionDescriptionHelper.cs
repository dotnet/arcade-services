// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;

namespace ProductConstructionService.Cli.Operations;

/// <summary>
/// Interface for generating human-readable subscription descriptions.
/// </summary>
internal interface ISubscriptionDescriptionHelper
{
    /// <summary>
    /// Gets a human-readable description of a subscription by its ID.
    /// If the subscription cannot be fetched, returns just the subscription ID.
    /// </summary>
    Task<string> GetSubscriptionDescriptionAsync(Guid subscriptionId);
}

/// <summary>
/// Helper class for generating human-readable subscription descriptions.
/// Caches subscription lookups to avoid redundant API calls.
/// </summary>
internal class SubscriptionDescriptionHelper : ISubscriptionDescriptionHelper
{
    private readonly IProductConstructionServiceApi _client;
    private readonly Dictionary<Guid, string> _cache = [];

    public SubscriptionDescriptionHelper(IProductConstructionServiceApi client)
    {
        _client = client;
    }

    /// <summary>
    /// Gets a human-readable description of a subscription by its ID.
    /// If the subscription cannot be fetched, returns just the subscription ID.
    /// Results are cached to avoid redundant API calls.
    /// </summary>
    public async Task<string> GetSubscriptionDescriptionAsync(Guid subscriptionId)
    {
        if (_cache.TryGetValue(subscriptionId, out var cachedDescription))
        {
            return cachedDescription;
        }

        string description;
        try
        {
            var subscription = await _client.Subscriptions.GetSubscriptionAsync(subscriptionId);
            description = GetSubscriptionDescription(subscription);
        }
        catch (RestApiException)
        {
            // If we can't fetch the subscription (e.g., it was deleted), fall back to just the ID
            description = subscriptionId.ToString();
        }

        _cache[subscriptionId] = description;
        return description;
    }

    /// <summary>
    /// Formats a subscription as a human-readable string.
    /// Format: source repo (channel) → target repo (target branch)
    /// </summary>
    public static string GetSubscriptionDescription(Subscription subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        
        var channelName = subscription.Channel?.Name ?? "unknown channel";
        return $"{subscription.SourceRepository} ({channelName}) → {subscription.TargetRepository} ({subscription.TargetBranch})";
    }
}
