// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Identity;

namespace ProductConstructionService.Api.Queue;

public static class QueueConfiguration
{
    public const string JobQueueConfigurationKey = $"{JobProcessorOptions.ConfigurationKey}:JobQueueName";

    public static void AddWorkitemQueues(this WebApplicationBuilder builder, DefaultAzureCredential credential)
    {
        builder.AddAzureQueueService("queues", (settings) => { settings.Credential = credential; });

        var queueName = builder.Configuration[JobQueueConfigurationKey] ??
            throw new ArgumentException($"{JobQueueConfigurationKey} missing from the configuration");

        // When running the service locally, the JobsProcessor should start in the Working state
        if (builder.Environment.IsDevelopment())
        {
            builder.Services.AddSingleton(new JobsProcessorScopeManager(true));
        }
        else
        {
            builder.Services.AddSingleton(new JobsProcessorScopeManager(false));
        }
        builder.Services.Configure<JobProcessorOptions>(
            builder.Configuration.GetSection(JobProcessorOptions.ConfigurationKey));
        builder.Services.AddTransient(sp =>
            ActivatorUtilities.CreateInstance<JobProducerFactory>(sp, queueName));
        builder.Services.AddHostedService<JobsProcessor>();
    }
}
