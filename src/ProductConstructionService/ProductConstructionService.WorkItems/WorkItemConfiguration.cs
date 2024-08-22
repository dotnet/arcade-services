// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using Azure.Identity;
using Azure.Storage.Queues;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProductConstructionService.Common;

namespace ProductConstructionService.WorkItems;

public static class WorkItemConfiguration
{
    public const string WorkItemQueueNameConfigurationKey = "WorkItemQueueName";

    internal static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static void AddWorkItemQueues(this IHostApplicationBuilder builder, DefaultAzureCredential credential, bool waitForInitialization)
    {
        builder.AddWorkItemProducerFactory(credential);

        // When running the service locally, the WorkItemProcessor should start in the Working state
        builder.Services.AddSingleton(sp =>
            new WorkItemScopeManager(waitForInitialization, sp, sp.GetRequiredService<ILogger<WorkItemScopeManager>>()));

        builder.Configuration[$"{WorkItemConsumerOptions.ConfigurationKey}:${WorkItemQueueNameConfigurationKey}"] =
            builder.Configuration.GetRequiredValue(WorkItemQueueNameConfigurationKey);
        builder.Services.Configure<WorkItemConsumerOptions>(
            builder.Configuration.GetSection(WorkItemConsumerOptions.ConfigurationKey));
        builder.Services.AddHostedService<WorkItemConsumer>();
    }

    public static void AddWorkItemProducerFactory(this IHostApplicationBuilder builder, DefaultAzureCredential credential)
    {
        builder.AddAzureQueueClient("queues", settings => settings.Credential = credential);

        var queueName = builder.Configuration.GetRequiredValue(WorkItemQueueNameConfigurationKey);

        builder.Services.AddTransient<IWorkItemProducerFactory>(sp =>
            ActivatorUtilities.CreateInstance<WorkItemProducerFactory>(sp, queueName));
    }

    // When running locally, create the workitem queue, if it doesn't already exist
    public static async Task UseLocalWorkItemQueues(this IServiceProvider serviceProvider, string queueName)
    {
        var queueServiceClient = serviceProvider.GetRequiredService<QueueServiceClient>();
        var queueClient = queueServiceClient.GetQueueClient(queueName);
        await queueClient.CreateIfNotExistsAsync();
    }

    public static void AddWorkItemProcessor<TWorkItem, TProcessor>(
            this IServiceCollection services,
            Func<IServiceProvider, TProcessor>? factory = null)
        where TWorkItem : WorkItem
        where TProcessor : class, IWorkItemProcessor
    {
        services.TryAddSingleton<WorkItemProcessorRegistrations>();

        if (factory != null)
        {
            services.TryAddTransient(factory);
        }
        else
        {
            services.TryAddTransient<TProcessor>();
        }

        services.TryAddKeyedTransient(typeof(TWorkItem).Name, (sp, _) => (IWorkItemProcessor)sp.GetRequiredService(typeof(TProcessor)));
        services.Configure<WorkItemProcessorRegistrations>(registrations =>
        {
            registrations.RegisterProcessor<TWorkItem, TProcessor>();
        });
    }
}
