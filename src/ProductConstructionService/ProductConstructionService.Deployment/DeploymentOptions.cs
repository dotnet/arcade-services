// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Identity;
using Azure.ResourceManager;
using CommandLine;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using ProductConstructionService.Common;
using StackExchange.Redis;

namespace ProductConstructionService.Deployment;
public class DeploymentOptions
{
    [Option("subscriptionId", Required = true, HelpText = "Azure subscription ID")]
    public required string SubscriptionId { get; init; }
    [Option("resourceGroupName", Required = true, HelpText = "Resource group name")]
    public required string ResourceGroupName { get; init; }
    [Option("containerAppName", Required = true, HelpText = "Container app name")]
    public required string ContainerAppName { get; init; }
    [Option("newImageTag", Required = true, HelpText = "New image tag")]
    public required string NewImageTag { get; init; }
    [Option("containerRegistryName", Required = true, HelpText = "Container registry name")]
    public required string ContainerRegistryName { get; init; }
    [Option("workspaceName", Required = true, HelpText = "Workspace name")]
    public required string WorkspaceName { get; init; }
    [Option("imageName", Required = true, HelpText = "Image name")]
    public required string ImageName { get; init; }
    [Option("containerJobNames", Required = true, HelpText = "Container job names")]
    public required string ContainerJobNames { get; init; }
    [Option("azCliPath", Required = true, HelpText = "Path to az.cmd")]
    public required string AzCliPath { get; init; }
    [Option("isCi", Required = true, HelpText = "Is running in CI")]
    public required bool IsCi { get; init; }
    [Option("entraAppId", Required = true, HelpText = "Entra app ID")]
    public required string EntraAppId { get; init; }
    [Option("redisConnectionString", Required = true, HelpText = "Redis Cache connection string")]
    public required string RedisConnectionString { get; init; }

    public async Task<IServiceCollection> RegisterServices(IServiceCollection services)
    {
        services.AddLogging(logging => logging.AddConsole());

        services.AddTransient<IProcessManager>(sp => new ProcessManager(sp.GetRequiredService<ILogger<ProcessManager>>(), string.Empty));
        DefaultAzureCredential credential = new();
        services.AddTransient(_ => credential);
        services.AddTransient<ArmClient>(sp => new(sp.GetRequiredService<DefaultAzureCredential>()));

        var redisConfig = ConfigurationOptions.Parse(RedisConnectionString);
        await redisConfig.ConfigureForAzureWithTokenCredentialAsync(credential);

        services.AddSingleton(_ => redisConfig);
        services.AddSingleton<IRedisCacheFactory, RedisCacheFactory>();

        services.AddSingleton(this);
        
        return services;
    }
}
