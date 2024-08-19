// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Identity;
using ProductConstructionService.Common;

namespace ProductConstructionService.Api.Queue;

internal static class QueueConfiguration
{
    public const string WorkItemQueueNameConfigurationKey = $"{WorkItemConsumerOptions.ConfigurationKey}:WorkItemQueueName";

    public static void AddWorkitemQueues(this WebApplicationBuilder builder, DefaultAzureCredential credential, bool waitForInitialization)
    {
        builder.AddAzureQueueClient("queues", settings => settings.Credential = credential);

        var queueName = builder.Configuration.GetRequiredValue(WorkItemQueueNameConfigurationKey);

        // When running the service locally, the JobsProcessor should start in the Working state
        builder.Services.AddSingleton(sp => ActivatorUtilities.CreateInstance<WorkItemScopeManager>(sp, waitForInitialization));
        builder.Services.Configure<WorkItemConsumerOptions>(
            builder.Configuration.GetSection(WorkItemConsumerOptions.ConfigurationKey));
        builder.Services.AddTransient(sp =>
            ActivatorUtilities.CreateInstance<WorkItemProducerFactory>(sp, queueName));
        builder.Services.AddHostedService<WorkItemConsumer>();
    }
}
