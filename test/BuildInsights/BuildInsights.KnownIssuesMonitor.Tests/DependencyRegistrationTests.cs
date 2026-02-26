// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using BuildInsights.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.DotNet.Internal.DependencyInjection.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using ProductConstructionService.Common;

namespace BuildInsights.KnownIssuesMonitor.Tests;

[TestFixture]
public class DependencyRegistrationTests
{
    [Test]
    public void AreDependenciesRegistered()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", Environments.Development);
        Environment.SetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING", "InstrumentationKey=value1");

        var builder = WebApplication.CreateBuilder();
        builder.Configuration[BuildInsightsConfiguration.ConfigurationKeys.DatabaseConnectionString] = "sql:3555";
        builder.Configuration[BuildInsightsConfiguration.ConfigurationKeys.RedisConnectionString] = "localhost:6379";
        builder.Configuration[DataProtection.DataProtectionKeyUri] = "https://keyvault.azure.com/secret/key";
        builder.Configuration[DataProtection.DataProtectionKeyBlobUri] = "https://blobs.azure.com/secret/key";
        builder.Configuration[BuildInsightsConfiguration.ConfigurationKeys.WorkItemQueueName] = "test-queue";
        builder.Configuration[BuildInsightsConfiguration.ConfigurationKeys.SpecialWorkItemQueueName] = "test-special-queue";
        builder.Configuration[BuildInsightsConfiguration.ConfigurationKeys.WorkItemConsumerCount] = "1";
        builder.Configuration[$"{BuildInsightsConfiguration.ConfigurationKeys.GitHubApp}:AppId"] = "12345";

        DependencyInjectionValidation.IsDependencyResolutionCoherent(
            s =>
            {
                foreach (ServiceDescriptor descriptor in builder.Services)
                {
                    s.Add(descriptor);
                }
            },
            out string message,
            additionalExemptTypes:
            [
                "Microsoft.Extensions.Azure.AzureClientsGlobalOptions",
                "Microsoft.Extensions.ServiceDiscovery.Configuration.ConfigurationServiceEndPointResolverProvider",
                "Microsoft.Extensions.ServiceDiscovery.Http.ServiceDiscoveryHttpMessageHandlerFactory",
                "Microsoft.Extensions.ServiceDiscovery.ServiceEndPointWatcherFactory",
                "Microsoft.Extensions.Hosting.ConsoleLifetimeOptions",
            ])
        .Should().BeTrue(message);
    }
}
