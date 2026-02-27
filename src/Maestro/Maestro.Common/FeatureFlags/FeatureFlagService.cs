// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Logging;
using Maestro.Common.Cache;
using System.Text.Json;

namespace Maestro.Common.FeatureFlags;

/// <summary>
/// Implementation of the feature flag service using Redis as the backing store.
/// </summary>
public class FeatureFlagService : IFeatureFlagService
{
    private const string KeyPrefix = "FeatureFlags";
    private static readonly TimeSpan? DefaultExpiration = null; // Never expire by default
    
    private readonly IRedisCacheFactory _redisCacheFactory;
    private readonly ILogger<FeatureFlagService> _logger;

    public FeatureFlagService(IRedisCacheFactory redisCacheFactory, ILogger<FeatureFlagService> logger)
    {
        _redisCacheFactory = redisCacheFactory;
        _logger = logger;
    }

    public async Task<FeatureFlagResponse> SetFlagAsync(
        Guid subscriptionId,
        FeatureFlag flag,
        string value,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var key = GetRedisKey(subscriptionId, flag);
            var cache = _redisCacheFactory.Create(key);
            
            var flagValue = new FeatureFlagValue(
                subscriptionId,
                flag.Name,
                value,
                DateTimeOffset.UtcNow);

            var json = JsonSerializer.Serialize(flagValue);
            await cache.SetAsync(json, expiry ?? DefaultExpiration);

            _logger.LogInformation("Set feature flag {FlagName} = {Value} for subscription {SubscriptionId}", 
                flag.Name, value, subscriptionId);

            return new FeatureFlagResponse(true, "Feature flag set successfully", flagValue);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set feature flag {FlagName} for subscription {SubscriptionId}", 
                flag.Name, subscriptionId);
            return new FeatureFlagResponse(false, $"Failed to set feature flag: {ex.Message}");
        }
    }

    public async Task<bool> IsFeatureOnAsync(
        Guid subscriptionId,
        FeatureFlag flag,
        CancellationToken cancellationToken = default)
    {
        var flagValue = await GetFlagAsync(subscriptionId, flag, cancellationToken);
        return flagValue?.Value.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false;
    }

    public async Task<FeatureFlagValue?> GetFlagAsync(
        Guid subscriptionId,
        FeatureFlag flag,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var key = GetRedisKey(subscriptionId, flag);
            var cache = _redisCacheFactory.Create(key);
            
            var json = await cache.TryGetAsync();
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            var flagValue = JsonSerializer.Deserialize<FeatureFlagValue>(json);
            
            return flagValue;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get feature flag {FlagName} for subscription {SubscriptionId}", 
                flag.Name, subscriptionId);
            return null;
        }
    }

    public async Task<IReadOnlyList<FeatureFlagValue>> GetFlagsAsync(
        Guid subscriptionId,
        CancellationToken cancellationToken = default)
    {
        var pattern = GetRedisKeyPattern(subscriptionId);
        var cache = _redisCacheFactory.Create("");
            
        var flags = new List<FeatureFlagValue>();
            
        await foreach (var key in cache.GetKeysAsync(pattern))
        {
            var flagCache = _redisCacheFactory.Create(key);
            var json = await flagCache.TryGetAsync();
                
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var flagValue = JsonSerializer.Deserialize<FeatureFlagValue>(json);
                    if (flagValue != null)
                    {
                        flags.Add(flagValue);
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize feature flag from key {Key}", key);
                }
            }
        }

        return flags;
    }

    public async Task<bool> RemoveFlagAsync(
        Guid subscriptionId,
        FeatureFlag flag,
        CancellationToken cancellationToken = default)
    {
        var key = GetRedisKey(subscriptionId, flag);
        var cache = _redisCacheFactory.Create(key);
            
        var removed = await cache.TryDeleteAsync();
            
        if (removed)
        {
            _logger.LogInformation("Removed feature flag {FlagName} for subscription {SubscriptionId}", 
                flag.Name, subscriptionId);
        }

        return removed;
    }

    public async Task<IReadOnlyList<FeatureFlagValue>> GetAllFlagsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var pattern = $"{KeyPrefix}_*";
            var cache = _redisCacheFactory.Create("");
            
            var flags = new List<FeatureFlagValue>();
            
            await foreach (var key in cache.GetKeysAsync(pattern))
            {
                var flagCache = _redisCacheFactory.Create(key);
                var json = await flagCache.TryGetAsync();
                
                if (!string.IsNullOrEmpty(json))
                {
                    try
                    {
                        var flagValue = JsonSerializer.Deserialize<FeatureFlagValue>(json);
                        if (flagValue != null)
                        {
                            flags.Add(flagValue);
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex, "Failed to deserialize feature flag from key {Key}", key);
                    }
                }
            }

            return flags;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get all feature flags");
            return new List<FeatureFlagValue>();
        }
    }

    public async Task<IReadOnlyList<FeatureFlagValue>> GetSubscriptionsWithFlagAsync(
        FeatureFlag flag,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var pattern = $"{KeyPrefix}_*:{flag.Name}";
            var cache = _redisCacheFactory.Create("");
            
            var flags = new List<FeatureFlagValue>();
            
            await foreach (var key in cache.GetKeysAsync(pattern))
            {
                var flagCache = _redisCacheFactory.Create(key);
                var json = await flagCache.TryGetAsync();
                
                if (!string.IsNullOrEmpty(json))
                {
                    try
                    {
                        var flagValue = JsonSerializer.Deserialize<FeatureFlagValue>(json);
                        if (flagValue != null)
                        {
                            flags.Add(flagValue);
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex, "Failed to deserialize feature flag from key {Key}", key);
                    }
                }
            }

            _logger.LogInformation("Found {Count} subscriptions with feature flag {FlagName}", flags.Count, flag.Name);
            return flags;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get subscriptions with feature flag {FlagName}", flag.Name);
            return new List<FeatureFlagValue>();
        }
    }

    public async Task<int> RemoveFlagFromAllSubscriptionsAsync(
        FeatureFlag flag,
        CancellationToken cancellationToken = default)
    {
        var pattern = $"{KeyPrefix}_*:{flag.Name}";
        var cache = _redisCacheFactory.Create("");
            
        var removedCount = 0;
            
        try
        {
            await foreach (var key in cache.GetKeysAsync(pattern))
            {
                var flagCache = _redisCacheFactory.Create(key);
                var removed = await flagCache.TryDeleteAsync();
                if (removed)
                {
                    removedCount++;
                }
            }

            _logger.LogInformation("Removed feature flag {FlagName} from {Count} subscriptions", flag.Name, removedCount);
            return removedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove feature flag {FlagName} from subscriptions after removing {Count} entries", flag.Name, removedCount);
            throw; // Let the exception bubble up as suggested in the PR comments
        }
    }

    private static string GetRedisKey(Guid subscriptionId, FeatureFlag flag)
    {
        return $"{KeyPrefix}_{subscriptionId}:{flag.Name}";
    }

    private static string GetRedisKeyPattern(Guid subscriptionId)
    {
        return $"{KeyPrefix}_{subscriptionId}:*";
    }
}
