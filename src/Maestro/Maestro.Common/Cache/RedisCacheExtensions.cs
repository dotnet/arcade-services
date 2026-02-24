// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Azure.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace Maestro.Common.Cache;

public static class RedisCacheExtensions
{
    public static async Task AddRedisCache(
        this IHostApplicationBuilder builder,
        string redisConnectionString,
        string? managedIdentityId)
    {
        var redisConfig = ConfigurationOptions.Parse(redisConnectionString);
        if (!string.IsNullOrEmpty(managedIdentityId))
        {
            AzureCacheOptions azureOptions = new();
            if (managedIdentityId != "system")
            {
                azureOptions.ClientId = managedIdentityId;
            }

            await redisConfig.ConfigureForAzureAsync(azureOptions);
        }

        builder.Services.AddSingleton(redisConfig);
        builder.Services.AddSingleton<IRedisCacheFactory, RedisCacheFactory>();
        builder.Services.AddSingleton<IRedisCacheClient, RedisCacheClient>();
        builder.Services.AddSingleton<IDistributedLock, DistributedLock>();
    }
}
