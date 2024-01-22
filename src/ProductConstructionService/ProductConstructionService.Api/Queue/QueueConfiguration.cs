﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.Api.Queue;

public static class QueueConfiguration
{
    public static void AddWorkitemQueues(this WebApplicationBuilder builder)
    {
        builder.AddAzureQueueService("queues");

        var queueName = builder.Configuration["PcsJobQueueName"] ??
            throw new ArgumentException("PcsJobQueueName missing from the configuration");

        builder.Services.AddSingleton<WorkItemProcessorStatus>();
        builder.Services.AddTransient(sp =>
            ActivatorUtilities.CreateInstance<PcsJobProducerFactory>(sp, queueName));
        builder.Services.AddHostedService(sp => 
            ActivatorUtilities.CreateInstance<WorkItemProcessor>(sp, queueName));
    }
}