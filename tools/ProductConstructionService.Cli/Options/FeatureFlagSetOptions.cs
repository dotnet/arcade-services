// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Identity;
using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ProductConstructionService.Common;
using ProductConstructionService.Cli.Operations;
using StackExchange.Redis;

namespace ProductConstructionService.Cli.Options;

[Verb("feature-flag-set", HelpText = "Set a feature flag for a subscription")]
internal class FeatureFlagSetOptions : Options
{
    [Option("subscription-id", Required = true, HelpText = "Subscription ID")]
    public required string SubscriptionId { get; init; }

    [Option("flag", Required = true, HelpText = "Feature flag name")]
    public required string FlagName { get; init; }

    [Option("value", Required = true, HelpText = "Feature flag value")]
    public required string Value { get; init; }

    [Option("expiry-days", Required = false, HelpText = "Number of days until the flag expires")]
    public int? ExpiryDays { get; init; }

    [Option("redis-connection-string", Required = true, HelpText = "Redis Cache connection string")]
    public required string RedisConnectionString { get; init; }

    public override IOperation GetOperation(IServiceProvider sp) => ActivatorUtilities.CreateInstance<FeatureFlagSetOperation>(sp, this);

    public override async Task<IServiceCollection> RegisterServices(IServiceCollection services)
    {
        var credential = new DefaultAzureCredential();
        services.AddSingleton(credential);

        var redisConfig = ConfigurationOptions.Parse(RedisConnectionString);
        await redisConfig.ConfigureForAzureWithTokenCredentialAsync(credential);

        services.AddSingleton(redisConfig);
        services.AddSingleton<IRedisCacheFactory, RedisCacheFactory>();
        services.AddSingleton<IFeatureFlagService, FeatureFlagService>();
        services.AddSingleton(this);

        return await base.RegisterServices(services);
    }
}