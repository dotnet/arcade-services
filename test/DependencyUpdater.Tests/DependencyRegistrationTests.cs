// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using FluentAssertions;
using Microsoft.DotNet.Internal.DependencyInjection.Testing;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ServiceFabric.Data;
using Moq;
using NUnit.Framework;

namespace DependencyUpdater.Tests;

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

                // The "IReliableStateManager" is provided by stateful services
                s.AddSingleton(Mock.Of<IReliableStateManager>());

                s.AddScoped<DependencyUpdater>();
            },
            out string message).Should().BeTrue(message);
    }
}
