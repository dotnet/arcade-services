// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.AppContainers;
using Azure.Storage.Queues;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using ProductConstructionService.Common;

namespace ProductConstructionService.WorkItems;

public static class WorkItemConfiguration
{
    public const string ReplicaNameKey = "CONTAINER_APP_REPLICA_NAME";
    public const string SubscriptionIdKey = "SubscriptionId";
    public const string ResourceGroupNameKey = "ResourceGroupName";
    public const string ContainerAppNameKey = "ContainerAppName";
    public const int PollingRateSeconds = 10;
    public const string LocalReplicaName = "localReplica";


    internal static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static void AddWorkItemQueues(
        this IHostApplicationBuilder builder,
        TokenCredential credential,
        bool waitForInitialization,
        Dictionary<string, (int Count, string WorkItemType)> workItemConsumers)
    {
        builder.Services.AddSingleton(sp => ActivatorUtilities.CreateInstance<WorkItemProcessorStateCache>(
            sp,
            builder.Configuration[ReplicaNameKey] ?? LocalReplicaName));
        builder.Services.AddSingleton(sp => ActivatorUtilities.CreateInstance<WorkItemProcessorState>(
            sp,
            new AutoResetEvent(false)));
        builder.Services.AddSingleton(sp => ActivatorUtilities.CreateInstance<WorkItemScopeManager>(
            sp,
            PollingRateSeconds));

        builder.Services.Configure<WorkItemConsumerOptions>(
            builder.Configuration.GetSection(WorkItemConsumerOptions.ConfigurationKey));

        foreach (var consumer in workItemConsumers)
        {
            builder.RegisterWorkItemConsumers(
                count: consumer.Value.Count,
                type: consumer.Value.WorkItemType,
                queueName: consumer.Key);
        }

        builder.Services.AddTransient<IReminderManagerFactory, ReminderManagerFactory>();
        if (builder.Environment.IsDevelopment())
        {
            builder.Services.AddTransient<IReplicaWorkItemProcessorStateCacheFactory, LocalReplicaWorkItemProcessorStateCacheFactory>();
        }
        else
        {
            builder.Services.AddTransient(sp =>
                new ArmClient(credential)
                    .GetSubscriptionResource(new ResourceIdentifier($"/subscriptions/{builder.Configuration.GetRequiredValue(SubscriptionIdKey)}"))
                    .GetResourceGroups().Get(builder.Configuration.GetRequiredValue(ResourceGroupNameKey)).Value
                    .GetContainerApp(builder.Configuration.GetRequiredValue(ContainerAppNameKey)).Value
            );
            builder.Services.AddTransient<IReplicaWorkItemProcessorStateCacheFactory, ReplicaWorkItemProcessorStateCache>();
        }
    }

    // When running locally, create the workitem queue, if it doesn't already exist
    public static async Task UseLocalWorkItemQueues(this IServiceProvider serviceProvider, string[] queueNames)
    {
        var queueServiceClient = serviceProvider.GetRequiredService<QueueServiceClient>();

        foreach (var queueName in queueNames)
        {
            var queueClient = queueServiceClient.GetQueueClient(queueName);
            await queueClient.CreateIfNotExistsAsync();
        }
    }

    public static void AddWorkItemProcessor<TWorkItem, TProcessor>(
            this IServiceCollection services,
            Func<IServiceProvider, TProcessor>? factory = null)
        where TWorkItem : WorkItem
        where TProcessor : class, IWorkItemProcessor
    {
        // We need IOption<WorkItemProcessorRegistrations> where we add the registrations
        services.AddOptions();
        services.TryAddSingleton<WorkItemProcessorRegistrations>();

        var diKey = typeof(TWorkItem).Name;
        if (factory != null)
        {
            services.TryAddKeyedTransient<IWorkItemProcessor>(diKey, (sp, _) => factory(sp));
        }
        else
        {
            services.TryAddKeyedTransient<IWorkItemProcessor, TProcessor>(diKey);
        }

        services.Configure<WorkItemProcessorRegistrations>(registrations =>
        {
            registrations.RegisterProcessor<TWorkItem, TProcessor>();
        });
    }

    public static void AddWorkItemProducerFactory(
        this IHostApplicationBuilder builder,
        TokenCredential credential,
        string defaulQueueName,
        string specialQueueName)
    {
        builder.AddAzureQueueServiceClient("queues", settings => settings.Credential = credential);
        builder.Services.AddTransient<IWorkItemProducerFactory>(sp =>
            ActivatorUtilities.CreateInstance<WorkItemProducerFactory>(sp, defaulQueueName, specialQueueName));
    }

    private static void RegisterWorkItemConsumers(
        this IHostApplicationBuilder builder,
        int count,
        string type,
        string queueName)
    {
        for (int i = 0; i < count; i++)
        {
            var consumerId = $"{type}WorkItemConsumer_{i}";

            // https://github.com/dotnet/runtime/issues/38751
            builder.Services.AddSingleton<IHostedService, WorkItemConsumer>(
                p => ActivatorUtilities.CreateInstance<WorkItemConsumer>(p, consumerId, queueName));
        }
    }
}
