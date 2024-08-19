// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.DotNet.Internal.DependencyInjection.Testing;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace ProductConstructionService.LongestBuildPathUpdater.Tests;

[TestFixture]
public class Tests
{
    [Test]
    public void AreDependenciesRegistered()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Configuration["BuildAssetRegistrySqlConnectionString"] = "barConnectionString";

        builder.ConfigureLongestBuildPathUpdater(new InMemoryChannel(), true);

        DependencyInjectionValidation.IsDependencyResolutionCoherent(s =>
        {
            foreach (var descriptor in builder.Services)
            {
                s.Add(descriptor);
            }
        },
        out var message).Should().BeTrue(message);
    }
}
