// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using FluentAssertions;
using Microsoft.DotNet.Internal.DependencyInjection.Testing;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace SubscriptionActorService.Tests;

[TestFixture]
public class DependencyRegistrationTests
{
    [Test]
    [Ignore("Needs https://github.com/dotnet/dnceng-shared/pull/42")]
    public void AreDependenciesRegistered()
    {
        DependencyInjectionValidation.IsDependencyResolutionCoherent(s =>
            {
                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "XUNIT");
                ServiceHost.ConfigureDefaultServices(s);
                Program.Configure(s);
                s.AddScoped<SubscriptionActor>();
                s.AddScoped<PullRequestActor>();
            },
            out string message).Should().BeTrue(message);
    }
}
