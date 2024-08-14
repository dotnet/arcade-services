// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.DotNet.Internal.DependencyInjection.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using ProductConstructionService.Api.Configuration;
using ProductConstructionService.Api.VirtualMonoRepo;

namespace ProductConstructionService.Api.Tests;

public class DependencyRegistrationTests
{
    [Test]
    public void AreDependenciesRegistered()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", Environments.Staging);

        var builder = WebApplication.CreateBuilder();

        builder.Configuration[VmrConfiguration.VmrPathKey] = "vmrPath";
        builder.Configuration[VmrConfiguration.TmpPathKey] = "tmpPath";
        builder.Configuration[VmrConfiguration.VmrUriKey] = "https://vmr.com/uri";
        builder.Configuration[PcsStartup.ConfigurationKeys.GitHubClientId] = "clientId";
        builder.Configuration[PcsStartup.ConfigurationKeys.GitHubClientSecret] = "clientSecret";
        builder.Configuration[PcsStartup.ConfigurationKeys.DatabaseConnectionString] = "connectionString";
        builder.Configuration[DataProtection.DataProtectionKeyUri] = "https://keyvault.azure.com/secret/key";
        builder.Configuration[DataProtection.DataProtectionKeyBlobUri] = "https://blobs.azure.com/secret/key";

        DefaultAzureCredential credential = new();

        builder.ConfigurePcs(
            azureCredential: credential,
            initializeService: true,
            addKeyVault: false,
            addSwagger: true);

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
                "Maestro.Authentication.BarTokenAuthenticationHandler",
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
