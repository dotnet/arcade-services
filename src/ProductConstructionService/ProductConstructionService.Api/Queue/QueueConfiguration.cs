// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.Api.Queue;

public static class QueueConfiguration
{
    public const string JobQueueConfigurationKey = "JobQueueName";
    public const string JobProcessorEmptyQueueWaitConfigurationKey = "EmptyQueueWaitSeconds";
    public const string JobProcessorOffTimeCheckConfigurationKey = "JobProcessorOffTimeCheckSeconds";

    public static void AddWorkitemQueues(this WebApplicationBuilder builder)
    {
        builder.AddAzureQueueService("queues");

        var queueName = builder.Configuration[JobQueueConfigurationKey] ??
            throw new ArgumentException($"{JobQueueConfigurationKey} missing from the configuration");
        var emptyQueueWaitSeconds = builder.Configuration[JobProcessorEmptyQueueWaitConfigurationKey] ??
            throw new ArgumentException($"{JobProcessorEmptyQueueWaitConfigurationKey} missing from the configuration");

        builder.Services.AddSingleton<JobsProcessorStatus>();
        builder.Services.AddSingleton(new JobProcessorOptions(queueName, int.Parse(emptyQueueWaitSeconds)));
        builder.Services.AddTransient(sp =>
            ActivatorUtilities.CreateInstance<JobProducerFactory>(sp, queueName));
        builder.Services.AddHostedService<JobsProcessor>();
    }
}
