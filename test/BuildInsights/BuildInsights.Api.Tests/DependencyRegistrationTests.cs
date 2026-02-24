// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using Microsoft.DotNet.Internal.DependencyInjection.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NUnit.Framework;

namespace BuildInsights.Api.Tests;

[TestFixture]
public class DependencyRegistrationTests
{
    [Test]
    public async Task AreDependenciesRegistered()
    {
        var builder = ApiTestConfiguration.CreateTestHostBuilder();
        await builder.ConfigureBuildInsights(
            addKeyVault: false);

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
                // TODO
                "BuildInsights.AzureStorage.Cache.BlobClientFactory",
                "BuildInsights.BuildAnalysis.BuildAnalysisHistoryProvider",
                "BuildInsights.BuildAnalysis.BuildAnalysisProvider",
                "BuildInsights.BuildAnalysis.BuildAnalysisRepositoryConfigurationProvider",
                "BuildInsights.BuildAnalysis.BuildProcessingStatusStatusProvider",
                "BuildInsights.BuildAnalysis.CheckResultProvider",
                //"BuildInsights.KnownIssues.KnownIssuesHistoryProvider",
                "BuildInsights.QueueInsights.MatrixOfTruthService",
                "BuildInsights.QueueInsights.QueueInsightsService",
                "BuildInsights.Utilities.AzureDevOps.ThrottlingHeaderLoggingHandler",
                "BuildInsights.Utilities.AzureDevOps.VssConnectionProvider",
                "Microsoft.DotNet.GitHub.Authentication.GitHubTokenProvider",
                "Microsoft.DotNet.Services.Utility.RetryAfterHandler",
                "ProductConstructionService.Common.Telemetry.TelemetryRecorder",

                "Microsoft.Extensions.Azure.AzureClientsGlobalOptions",
                "Microsoft.Extensions.ServiceDiscovery.Configuration.ConfigurationServiceEndPointResolverProvider",
                "Microsoft.Extensions.ServiceDiscovery.Http.ServiceDiscoveryHttpMessageHandlerFactory",
                "Microsoft.Extensions.ServiceDiscovery.ServiceEndPointWatcherFactory",
                "Microsoft.Extensions.Hosting.ConsoleLifetimeOptions",
            ])
        .Should().BeTrue(message);
    }
}
