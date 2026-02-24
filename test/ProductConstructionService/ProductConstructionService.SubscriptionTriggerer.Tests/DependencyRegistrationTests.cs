// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.ApplicationInsights.Channel;
using Microsoft.DotNet.Internal.DependencyInjection.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using ProductConstructionService.SubscriptionTriggerer;

namespace SubscriptionTriggerer.Tests;

[TestFixture]
public class DependencyRegistrationTests
{
    [Test]
    public void AreDependenciesRegistered()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Configuration["ConnectionStrings:queues"] = "queueConnectionString";
        builder.Configuration["WorkItemQueueName"] = "queue";
        builder.Configuration["BuildAssetRegistrySqlConnectionString"] = "barConnectionString";

        builder.ConfigureSubscriptionTriggerer(new InMemoryChannel());

        DependencyInjectionValidation.IsDependencyResolutionCoherent(s =>
            {
                foreach (var descriptor in builder.Services)
                {
                    s.Add(descriptor);
                }
            },
            out var message,
            additionalExemptTypes: [
                "Microsoft.Extensions.Azure.AzureClientsGlobalOptions",
                "Microsoft.Extensions.Hosting.ConsoleLifetimeOptions"
            ])
            .Should().BeTrue(message);
    }
}
