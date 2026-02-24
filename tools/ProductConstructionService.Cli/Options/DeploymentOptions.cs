// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.AppContainers;
using Azure.ResourceManager.Resources;
using CommandLine;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ProductConstructionService.Common;
using ProductConstructionService.Cli.Operations;
using ProductConstructionService.WorkItems;
using StackExchange.Redis;
using Maestro.Common.Cache;

namespace ProductConstructionService.Cli.Options;

[Verb("deploy", HelpText = "Deploy PCS with the specified options")]
internal class DeploymentOptions : Options
{
    [Option("subscriptionId", Required = true, HelpText = "Azure subscription ID")]
    public required string SubscriptionId { get; init; }

    [Option("resourceGroupName", Required = true, HelpText = "Resource group name")]
    public required string ResourceGroupName { get; init; }

    [Option("containerAppName", Required = true, HelpText = "Container app name")]
    public required string ContainerAppName { get; init; }

    [Option("newImageTag", Required = true, HelpText = "New image tag")]
    public required string NewImageTag { get; init; }

    [Option("attempt", Required = false, HelpText = "Attempt number for the deployment")]
    public string? Attempt { get; init; }

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

    [Option("redisConnectionString", Required = true, HelpText = "Redis Cache connection string")]
    public required string RedisConnectionString { get; init; }

    public override IOperation GetOperation(IServiceProvider sp) => ActivatorUtilities.CreateInstance<DeploymentOperation>(sp, this);

    public override async Task<IServiceCollection> RegisterServices(IServiceCollection services)
    {
        services.AddTransient<IProcessManager>(sp => new ProcessManager(sp.GetRequiredService<ILogger<ProcessManager>>(), "git"));

        var credential = AzureAuthentication.GetCliCredential();
        services.AddSingleton(credential);
        services.AddTransient<ArmClient>(_ => new(credential));
        services.AddTransient<ResourceGroupResource>(sp =>
        {
            return new ArmClient(credential)
                .GetSubscriptionResource(new ResourceIdentifier($"/subscriptions/{SubscriptionId}"))
                .GetResourceGroups().Get(ResourceGroupName);
        });
        services.AddTransient(sp =>
            sp.GetRequiredService<ResourceGroupResource>().GetContainerApp(ContainerAppName).Value);
        services.AddTransient<IReplicaWorkItemProcessorStateCacheFactory, ReplicaWorkItemProcessorStateCache>();

        var redisConfig = ConfigurationOptions.Parse(RedisConnectionString);
        await redisConfig.ConfigureForAzureWithTokenCredentialAsync(credential);

        services.AddSingleton(redisConfig);
        services.AddSingleton<IRedisCacheFactory, RedisCacheFactory>();

        services.AddSingleton(this);

        return await base.RegisterServices(services);
    }
}
