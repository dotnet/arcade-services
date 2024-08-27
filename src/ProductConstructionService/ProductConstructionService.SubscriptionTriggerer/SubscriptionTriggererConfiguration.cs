// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.DataProviders;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProductConstructionService.Common;
using Azure.Identity;
using ProductConstructionService.WorkItems;
using Azure.Storage.Queues.Models;

namespace ProductConstructionService.SubscriptionTriggerer;

public static class SubscriptionTriggererConfiguration
{
    public static HostApplicationBuilder ConfigureSubscriptionTriggerer(
        this HostApplicationBuilder builder,
        ITelemetryChannel telemetryChannel)
    {
        DefaultAzureCredential credential = new(
            new DefaultAzureCredentialOptions
            {
                ManagedIdentityClientId = builder.Configuration[ProductConstructionServiceExtension.ManagedIdentityClientId]
            });

        builder.Services.RegisterLogging(telemetryChannel, builder.Environment.IsDevelopment());

        builder.AddBuildAssetRegistry();
        // TODO (https://github.com/dotnet/arcade-services/issues/3811) Use a fake WorkItemProducer untill we
        // add some kind of feature switch to trigger specific subscriptions
        //builder.AddWorkItemProducerFactory(credential);
        builder.Services.AddTransient<IWorkItemProducerFactory, FakeWorkItemProducerFacory>();

        builder.Services.AddTransient<DarcRemoteMemoryCache>();
        builder.Services.AddTransient<IProcessManager>(sp => ActivatorUtilities.CreateInstance<ProcessManager>(sp, "git"));
        builder.Services.AddTransient<IVersionDetailsParser, VersionDetailsParser>();

        builder.Services.AddTransient<SubscriptionTriggerer>();

        return builder;
    }
}

internal class FakeWorkItemProducer<T> : IWorkItemProducer<T> where T : WorkItem
{
    public Task DeleteWorkItemAsync(string messageId, string popReceipt)
    {
        return Task.CompletedTask;
    }

    public Task<SendReceipt> ProduceWorkItemAsync(T payload, TimeSpan delay = default)
    {
        return Task.FromResult(QueuesModelFactory.SendReceipt("fake", DateTimeOffset.Now, DateTimeOffset.Now, "fake", DateTimeOffset.Now));
    }
}

internal class FakeWorkItemProducerFacory : IWorkItemProducerFactory
{
    public IWorkItemProducer<T> CreateProducer<T>() where T : WorkItem => new FakeWorkItemProducer<T>();
}
