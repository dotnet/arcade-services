// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Identity;
using Azure.Storage.Queues;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProductConstructionService.Common;
using ProductConstructionService.WorkItems.WorkItemDefinitions;
using ProductConstructionService.WorkItems.WorkItemProcessors;

namespace ProductConstructionService.WorkItems;

public static class WorkItemConfiguration
{
    private const string WorkItemQueueNameConfigurationKey = $"{WorkItemConsumerOptions.ConfigurationKey}:WorkItemQueueName";

    public static void AddWorkItemQueues(this WebApplicationBuilder builder, DefaultAzureCredential credential, bool waitForInitialization)
    {
        builder.AddAzureQueueClient("queues", settings => settings.Credential = credential);

        var queueName = builder.Configuration.GetRequiredValue(WorkItemQueueNameConfigurationKey);

        // When running the service locally, the WorkItemProcessor should start in the Working state
        builder.Services.AddSingleton(sp => ActivatorUtilities.CreateInstance<WorkItemScopeManager>(sp, waitForInitialization));
        builder.Services.Configure<WorkItemConsumerOptions>(
            builder.Configuration.GetSection(WorkItemConsumerOptions.ConfigurationKey));
        builder.Services.AddTransient(sp =>
            ActivatorUtilities.CreateInstance<WorkItemProducerFactory>(sp, queueName));
        builder.Services.AddHostedService<WorkItemConsumer>();
    }

    // When running locally, create the workitem queue, if it doesn't already exist
    public static async Task UseLocalWorkItemQueues(this WebApplication app)
    {
        var queueServiceClient = app.Services.GetRequiredService<QueueServiceClient>();
        var queueClient = queueServiceClient.GetQueueClient(app.Configuration.GetRequiredValue(WorkItemQueueNameConfigurationKey));
        await queueClient.CreateIfNotExistsAsync();
    }

    public static void AddWorkItemProcessors(this IServiceCollection services)
    {
        services.RegisterWorkItemProcessor<CodeFlowWorkItem, CodeFlowWorkItemProcessor>();
    }

    private static void RegisterWorkItemProcessor<TWorkItem, TProcessor>(this IServiceCollection services)
        where TProcessor : class, IWorkItemProcessor
    {
        services.AddKeyedTransient<IWorkItemProcessor, TProcessor>(typeof(TWorkItem).Name);
    }
}
