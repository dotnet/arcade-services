// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Azure;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Maestro.ContainerApp.Queues;

internal static class QueueConfiguration
{
    public static void AddBackgroudQueueProcessors(this WebApplicationBuilder builder)
    {
        builder.Services.AddAzureClients(clientBuilder =>
        {
            var configSection = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true"
                ? builder.Configuration.GetSection("ConnectionStrings")
                : builder.Configuration.GetSection("ConnectionStringsNonDocker");

            clientBuilder.AddQueueServiceClient(configSection["AzureQueues"]);
            clientBuilder.ConfigureDefaults(options =>
            {
                options.Diagnostics.IsLoggingEnabled = false;
            });
        });

        var backgroundQueueName = builder.Configuration["BackgroundQueueName"]
            ?? throw new ArgumentException("Please configure the BackgroundQueueName setting");

        builder.Services.AddHostedService(sp
            => ActivatorUtilities.CreateInstance<BackgroundQueueProcessor>(sp, backgroundQueueName));
        builder.Services.TryAddTransient(sp
            => ActivatorUtilities.CreateInstance<QueueProducerFactory>(sp, backgroundQueueName));
    }
}
