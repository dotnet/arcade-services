// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.Api.Controllers;
using ProductConstructionService.Api.Telemetry;
using ProductConstructionService.Api.VirtualMonoRepo;
using ProductConstructionService.Api.Queue;
using Microsoft.AspNetCore.Builder;
using Azure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.DotNet.Internal.DependencyInjection.Testing;
using Microsoft.Extensions.DependencyInjection.Extensions;
using FluentAssertions;
using Microsoft.Extensions.Hosting;

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

        DefaultAzureCredential credential = new();

        builder.AddBuildAssetRegistry(_databaseConnectionString);
        builder.AddTelemetry();
        builder.AddVmrRegistrations(_vmrPath, _tmpPath);
        builder.AddGitHubClientFactory();

        builder.AddVmrInitialization(_vmrUri);
        builder.AddWorkitemQueues(credential, waitForInitialization: true);
        builder.AddEndpointAuthentication(requirePolicyRole: true);

        builder.Services.AddControllers().EnableInternalControllers();

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
            "Microsoft.Extensions.Azure.AzureClientsGlobalOptions"])
            .Should().BeTrue(message);
    }
}
