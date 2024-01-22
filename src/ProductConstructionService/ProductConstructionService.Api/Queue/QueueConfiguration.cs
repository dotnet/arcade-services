// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.Api.Queue;

public static class QueueConfiguration
{
    public const string PcsJobQueueConfigurationKey = "PcsJobQueueName";

    public static void AddWorkitemQueues(this WebApplicationBuilder builder)
    {
        builder.AddAzureQueueService("queues");

        var queueName = builder.Configuration[PcsJobQueueConfigurationKey] ??
            throw new ArgumentException($"{PcsJobQueueConfigurationKey} missing from the configuration");

        builder.Services.AddSingleton<PcsJobsProcessorStatus>();
        builder.Services.AddTransient(sp =>
            ActivatorUtilities.CreateInstance<PcsJobProducerFactory>(sp, queueName));
        builder.Services.AddHostedService(sp => 
            ActivatorUtilities.CreateInstance<PcsJobsProcessor>(sp, queueName));
    }
}
