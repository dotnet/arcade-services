// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.DotNet.Internal.DependencyInjection.Testing;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;

namespace ProductConstructionService.FeedCleaner.Tests;

[TestFixture]
public class DependencyRegistrationTest
{
    [Test]
    public void AreDependenciesRegistered()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Configuration["BuildAssetRegistrySqlConnectionString"] = "barConnectionString";

        builder.ConfigureFeedCleaner(new InMemoryChannel());

        DependencyInjectionValidation.IsDependencyResolutionCoherent(s =>
        {
            foreach (var descriptor in builder.Services)
            {
                s.Add(descriptor);
            }
        },
        out var message,
        additionalExemptTypes: [
            "Microsoft.Extensions.Hosting.ConsoleLifetimeOptions"
        ]).Should().BeTrue(message);
    }
}
