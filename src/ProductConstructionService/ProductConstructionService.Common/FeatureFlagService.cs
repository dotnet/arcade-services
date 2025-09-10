// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace ProductConstructionService.Common;

/// <summary>
/// Implementation of the feature flag service using Redis as the backing store.
/// </summary>
public class FeatureFlagService : IFeatureFlagService
{
    private const string KeyPrefix = "FeatureFlags";
    private static readonly TimeSpan DefaultExpiration = TimeSpan.FromDays(180);
    
    private readonly IRedisCacheFactory _redisCacheFactory;
    private readonly ILogger<FeatureFlagService> _logger;

    public FeatureFlagService(IRedisCacheFactory redisCacheFactory, ILogger<FeatureFlagService> logger)
    {
        _redisCacheFactory = redisCacheFactory;
        _logger = logger;
    }

    public async Task<FeatureFlagResponse> SetFlagAsync(
        Guid subscriptionId,
        string flagName,
        string value,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateFlag(flagName, value);
        if (!validation.Success)
        {
            return validation;
        }

        try
        {
            var key = GetRedisKey(subscriptionId, flagName);
            var cache = _redisCacheFactory.Create(key);
            
            var flagValue = new FeatureFlagValue(
                subscriptionId,
                flagName,
                value,
                expiry.HasValue ? DateTimeOffset.UtcNow.Add(expiry.Value) : null,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow);

            var json = JsonSerializer.Serialize(flagValue);
            await cache.SetAsync(json, expiry ?? DefaultExpiration);

            _logger.LogInformation("Set feature flag {FlagName} = {Value} for subscription {SubscriptionId}", 
                flagName, value, subscriptionId);

            return new FeatureFlagResponse(true, "Feature flag set successfully", flagValue);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set feature flag {FlagName} for subscription {SubscriptionId}", 
                flagName, subscriptionId);
            return new FeatureFlagResponse(false, $"Failed to set feature flag: {ex.Message}");
        }
    }

    public async Task<FeatureFlagValue?> GetFlagAsync(
        Guid subscriptionId,
        string flagName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var key = GetRedisKey(subscriptionId, flagName);
            var cache = _redisCacheFactory.Create(key);
            
            var json = await cache.TryGetAsync();
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            var flagValue = JsonSerializer.Deserialize<FeatureFlagValue>(json);
            
            // Check if flag has expired
            if (flagValue?.Expiry.HasValue == true && flagValue.Expiry.Value < DateTimeOffset.UtcNow)
            {
                await cache.TryDeleteAsync();
                _logger.LogInformation("Expired feature flag {FlagName} removed for subscription {SubscriptionId}", 
                    flagName, subscriptionId);
                return null;
            }

            return flagValue;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get feature flag {FlagName} for subscription {SubscriptionId}", 
                flagName, subscriptionId);
            return null;
        }
    }

    public async Task<IReadOnlyList<FeatureFlagValue>> GetFlagsForSubscriptionAsync(
        Guid subscriptionId,
        CancellationToken cancellationToken = default)
    {
        try
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
                            // Check if flag has expired
                            if (flagValue.Expiry.HasValue && flagValue.Expiry.Value < DateTimeOffset.UtcNow)
                            {
                                await flagCache.TryDeleteAsync();
                                _logger.LogInformation("Expired feature flag {FlagName} removed for subscription {SubscriptionId}", 
                                    flagValue.FlagName, subscriptionId);
                                continue;
                            }
                            
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
            _logger.LogError(ex, "Failed to get feature flags for subscription {SubscriptionId}", subscriptionId);
            return new List<FeatureFlagValue>();
        }
    }

    public async Task<bool> RemoveFlagAsync(
        Guid subscriptionId,
        string flagName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var key = GetRedisKey(subscriptionId, flagName);
            var cache = _redisCacheFactory.Create(key);
            
            var removed = await cache.TryDeleteAsync();
            
            if (removed)
            {
                _logger.LogInformation("Removed feature flag {FlagName} for subscription {SubscriptionId}", 
                    flagName, subscriptionId);
            }

            return removed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove feature flag {FlagName} for subscription {SubscriptionId}", 
                flagName, subscriptionId);
            return false;
        }
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
                            // Check if flag has expired
                            if (flagValue.Expiry.HasValue && flagValue.Expiry.Value < DateTimeOffset.UtcNow)
                            {
                                await flagCache.TryDeleteAsync();
                                _logger.LogInformation("Expired feature flag {FlagName} removed for subscription {SubscriptionId}", 
                                    flagValue.FlagName, flagValue.SubscriptionId);
                                continue;
                            }
                            
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

    public FeatureFlagResponse ValidateFlag(string flagName, string value)
    {
        if (string.IsNullOrWhiteSpace(flagName))
        {
            return new FeatureFlagResponse(false, "Flag name cannot be empty");
        }

        if (!FeatureFlags.IsValidFlag(flagName))
        {
            return new FeatureFlagResponse(false, $"Unknown feature flag: {flagName}. Valid flags are: {string.Join(", ", FeatureFlags.AllFlags.Keys)}");
        }

        var metadata = FeatureFlags.GetMetadata(flagName);
        if (metadata == null)
        {
            return new FeatureFlagResponse(false, $"No metadata found for flag: {flagName}");
        }

        // Validate value based on flag type
        switch (metadata.Type)
        {
            case FeatureFlagType.Boolean:
                if (!bool.TryParse(value, out _))
                {
                    return new FeatureFlagResponse(false, $"Invalid boolean value '{value}' for flag {flagName}. Expected: true or false");
                }
                break;
            
            case FeatureFlagType.Integer:
                if (!int.TryParse(value, out _))
                {
                    return new FeatureFlagResponse(false, $"Invalid integer value '{value}' for flag {flagName}");
                }
                break;
            
            case FeatureFlagType.Double:
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                {
                    return new FeatureFlagResponse(false, $"Invalid double value '{value}' for flag {flagName}");
                }
                break;
            
            case FeatureFlagType.String:
                // Any string is valid, but check for reasonable length
                if (value?.Length > 1000)
                {
                    return new FeatureFlagResponse(false, $"String value too long for flag {flagName}. Maximum length is 1000 characters");
                }
                break;
        }

        return new FeatureFlagResponse(true, "Valid flag");
    }

    private static string GetRedisKey(Guid subscriptionId, string flagName)
    {
        return $"{KeyPrefix}_{subscriptionId}:{flagName}";
    }

    private static string GetRedisKeyPattern(Guid subscriptionId)
    {
        return $"{KeyPrefix}_{subscriptionId}:*";
    }
}

/// <summary>
/// Client implementation for strongly-typed access to feature flags.
/// </summary>
public class FeatureFlagClient : IFeatureFlagClient
{
    private readonly IFeatureFlagService _featureFlagService;
    private readonly ILogger<FeatureFlagClient> _logger;
    private readonly Dictionary<string, FeatureFlagValue> _cache = new();
    private Guid? _subscriptionId;

    public FeatureFlagClient(IFeatureFlagService featureFlagService, ILogger<FeatureFlagClient> logger)
    {
        _featureFlagService = featureFlagService;
        _logger = logger;
    }

    public async Task InitializeAsync(Guid subscriptionId, CancellationToken cancellationToken = default)
    {
        _subscriptionId = subscriptionId;
        _cache.Clear();
        
        try
        {
            var flags = await _featureFlagService.GetFlagsForSubscriptionAsync(subscriptionId, cancellationToken);
            foreach (var flag in flags)
            {
                _cache[flag.FlagName] = flag;
            }
            
            _logger.LogDebug("Loaded {Count} feature flags for subscription {SubscriptionId}", 
                flags.Count, subscriptionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize feature flag client for subscription {SubscriptionId}", 
                subscriptionId);
        }
    }

    public async Task<bool> GetBooleanFlagAsync(string flagName, bool defaultValue = false)
    {
        var value = await GetFlagValueAsync(flagName);
        
        if (string.IsNullOrEmpty(value))
        {
            return defaultValue;
        }

        return bool.TryParse(value, out var result) ? result : defaultValue;
    }

    public async Task<string> GetStringFlagAsync(string flagName, string defaultValue = "")
    {
        var value = await GetFlagValueAsync(flagName);
        return string.IsNullOrEmpty(value) ? defaultValue : value;
    }

    public async Task<int> GetIntegerFlagAsync(string flagName, int defaultValue = 0)
    {
        var value = await GetFlagValueAsync(flagName);
        
        if (string.IsNullOrEmpty(value))
        {
            return defaultValue;
        }

        return int.TryParse(value, out var result) ? result : defaultValue;
    }

    public async Task<double> GetDoubleFlagAsync(string flagName, double defaultValue = 0.0)
    {
        var value = await GetFlagValueAsync(flagName);
        
        if (string.IsNullOrEmpty(value))
        {
            return defaultValue;
        }

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : defaultValue;
    }

    private async Task<string?> GetFlagValueAsync(string flagName)
    {
        if (_subscriptionId == null)
        {
            _logger.LogWarning("Feature flag client not initialized with a subscription ID");
            return null;
        }

        // Check cache first
        if (_cache.TryGetValue(flagName, out var cachedFlag))
        {
            // Check if flag has expired
            if (cachedFlag.Expiry.HasValue && cachedFlag.Expiry.Value < DateTimeOffset.UtcNow)
            {
                _cache.Remove(flagName);
                return null;
            }
            
            return cachedFlag.Value;
        }

        // Not in cache, check Redis directly
        try
        {
            var flag = await _featureFlagService.GetFlagAsync(_subscriptionId.Value, flagName);
            if (flag != null)
            {
                _cache[flagName] = flag;
                return flag.Value;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get feature flag {FlagName} for subscription {SubscriptionId}", 
                flagName, _subscriptionId);
        }

        return null;
    }
}