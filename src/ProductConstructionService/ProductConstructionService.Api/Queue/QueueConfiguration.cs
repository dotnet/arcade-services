// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Identity;
using ProductConstructionService.Api.Queue.JobProcessors;
using ProductConstructionService.Api.Queue.Jobs;
using ProductConstructionService.Api.Configuration;

namespace ProductConstructionService.Api.Queue;

internal static class QueueConfiguration
{
    public const string JobQueueNameConfigurationKey = $"{JobConsumerOptions.ConfigurationKey}:JobQueueName";

    public static void AddWorkitemQueues(this WebApplicationBuilder builder, DefaultAzureCredential credential, bool waitForInitialization)
    {
        builder.AddAzureQueueService("queues", settings => settings.Credential = credential);

        var queueName = builder.Configuration.GetRequiredValue(JobQueueNameConfigurationKey);

        // When running the service locally, the JobsProcessor should start in the Working state
        builder.Services.AddSingleton(sp => ActivatorUtilities.CreateInstance<JobScopeManager>(sp, waitForInitialization));
        builder.Services.Configure<JobConsumerOptions>(
            builder.Configuration.GetSection(JobConsumerOptions.ConfigurationKey));
        builder.Services.AddTransient(sp =>
            ActivatorUtilities.CreateInstance<JobProducerFactory>(sp, queueName));
        builder.Services.AddHostedService<JobConsumer>();

        // Register all job processors
        builder.Services.RegisterJobProcessor<TextJob, TextJobProcessor>();
        builder.Services.RegisterJobProcessor<CodeFlowJob, CodeFlowJobProcessor>();
    }
}

static file class JobProcessorExtensions
{
    public static void RegisterJobProcessor<TJob, TProcessor>(this IServiceCollection services)
        where TProcessor : class, IJobProcessor
    {
        services.AddKeyedTransient<IJobProcessor, TProcessor>(typeof(TJob).Name);
    }
}
