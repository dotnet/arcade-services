// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using Microsoft.DotNet.Internal.DependencyInjection.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProductConstructionService.DependencyFlow.Model;
using ProductConstructionService.DependencyFlow.PullRequestUpdaters;

namespace ProductConstructionService.Api.Tests;

public class DependencyRegistrationTests
{
    [Test]
    public async Task AreDependenciesRegistered()
    {
        var builder = ApiTestConfiguration.CreateTestHostBuilder();
        await builder.ConfigurePcs(
            addKeyVault: false,
            authRedis: false,
            addSwagger: true);

        builder.Services.AddTransient<NonBatchedDependencyPullRequestUpdater>();
        builder.Services.AddSingleton(new NonBatchedPullRequestUpdaterId(Guid.NewGuid()));
        builder.Services.AddTransient<BatchedDependencyPullRequestUpdater>();
        builder.Services.AddSingleton(new BatchedPullRequestUpdaterId("repo", "branch"));

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
                "ProductConstructionService.Api.Configuration.BarTokenAuthenticationHandler",
                "Microsoft.Extensions.ApiDescriptions.DocumentProvider",
                "Microsoft.Extensions.Azure.AzureClientsGlobalOptions",
                "Microsoft.Extensions.Hosting.ConsoleLifetimeOptions",
                "Microsoft.Extensions.ServiceDiscovery.Configuration.ConfigurationServiceEndPointResolverProvider",
                "Microsoft.Extensions.ServiceDiscovery.Http.ServiceDiscoveryHttpMessageHandlerFactory",
                "Microsoft.Extensions.ServiceDiscovery.ServiceEndPointWatcherFactory",
                "Microsoft.Identity.Web.Resource.MicrosoftIdentityIssuerValidatorFactory",
            ])
        .Should().BeTrue(message);
    }
}
