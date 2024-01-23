// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;

namespace ProductConstructionService.Api.Queue;

public static class QueueConfiguration
{
    public const string PcsJobQueueConfigurationKey = "PcsJobQueueName";
    public const string PcsJobProcessorEmptyQueueWaitConfigurationKey = "EmptyQueueWaitSeconds";
    public const string PcsJobProcessorOffTimeCheckConfigurationKey = "PcsJobProcessorOffTimeCheckSeconds";

    public static void AddWorkitemQueues(this WebApplicationBuilder builder)
    {
        builder.AddAzureQueueService("queues");

        var queueName = builder.Configuration[PcsJobQueueConfigurationKey] ??
            throw new ArgumentException($"{PcsJobQueueConfigurationKey} missing from the configuration");
        var emptyQueueWaitSeconds = builder.Configuration[PcsJobProcessorEmptyQueueWaitConfigurationKey] ??
            throw new ArgumentException($"{PcsJobProcessorEmptyQueueWaitConfigurationKey} missing from the configuration");
        var offTimeCheckSeconds = builder.Configuration[PcsJobProcessorOffTimeCheckConfigurationKey] ??
            throw new ArgumentException($"{PcsJobProcessorOffTimeCheckConfigurationKey} missing from the configuration");

        builder.Services.AddSingleton<PcsJobsProcessorStatus>();
        builder.Services.AddSingleton(new PcsJobProcessorOptions(queueName, int.Parse(emptyQueueWaitSeconds), int.Parse(offTimeCheckSeconds)));
        builder.Services.AddTransient(sp =>
            ActivatorUtilities.CreateInstance<PcsJobProducerFactory>(sp, queueName));
        builder.Services.AddHostedService<PcsJobsProcessor>();
    }
}
