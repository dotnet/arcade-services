// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Builder;
using Azure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.DotNet.Internal.DependencyInjection.Testing;
using Microsoft.Extensions.DependencyInjection.Extensions;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using ProductConstructionService.Api.Configuration;

namespace ProductConstructionService.Api.Tests;
public class DependencyRegistrationTests
{
    private readonly string _databaseConnectionString = "connectionString";
    private static readonly string _vmrPath = "vmrPath";
    private static readonly string _tmpPath = "tmpPath";
    private static readonly string _vmrUri = "vmrUri";
    private static readonly string _clientId = "clientId";
    private static readonly string _clientSecret = "clientSecret";

    [Test]
    public void AreDependenciesRegistered()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", Environments.Staging);

        var builder = WebApplication.CreateBuilder();

        builder.Configuration[PcsConfiguration.GitHubClientId] = _clientId;
        builder.Configuration[PcsConfiguration.GitHubClientSecret] = _clientSecret;
        builder.Configuration[PcsConfiguration.DatabaseConnectionString] = _databaseConnectionString;

        DefaultAzureCredential credential = new();

        builder.ConfigurePcs(
            vmrPath: _vmrPath,
            tmpPath: _tmpPath,
            vmrUri: _vmrUri,
            credential: credential,
            initializeService: true,
            addEndpointAuthentication: true);

        DependencyInjectionValidation.IsDependencyResolutionCoherent(s =>
        {
            foreach (ServiceDescriptor descriptor in builder.Services)
            {
                s.Add(descriptor);
            }
        },
        out string message,
        additionalExemptTypes: [
            "Microsoft.Extensions.Hosting.ConsoleLifetimeOptions",
            "Microsoft.Extensions.Azure.AzureClientsGlobalOptions",
            "Microsoft.Extensions.ServiceDiscovery.Abstractions.ConfigurationServiceEndPointResolverOptions",
            "Microsoft.Extensions.ServiceDiscovery.Abstractions.ServiceEndPointResolverOptions"
        ]).Should().BeTrue(message);
    }
}
