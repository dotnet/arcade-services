// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.Api.Queue;

public static class QueueConfiguration
{
    public const string JobQueueConfigurationKey = $"{JobProcessorOptions.ConfigurationKey}:JobQueueName";

    public static void AddWorkitemQueues(this WebApplicationBuilder builder)
    {
        builder.AddAzureQueueService("queues");

        var queueName = builder.Configuration[JobQueueConfigurationKey] ??
            throw new ArgumentException($"{JobQueueConfigurationKey} missing from the configuration");

        builder.Services.AddSingleton(new JobsProcessorScopeManager());
        builder.Services.Configure<JobProcessorOptions>(
            builder.Configuration.GetSection(JobProcessorOptions.ConfigurationKey));
        builder.Services.AddTransient(sp =>
            ActivatorUtilities.CreateInstance<JobProducerFactory>(sp, queueName));
        builder.Services.AddHostedService<JobsProcessor>();
    }
}
