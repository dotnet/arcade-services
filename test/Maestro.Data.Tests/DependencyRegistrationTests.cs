// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using FluentAssertions;
using Microsoft.DotNet.Internal.DependencyInjection.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using NUnit.Framework;

namespace Maestro.Data.Tests;

[TestFixture]
public class DependencyRegistrationTests
{
    [Test]
    [Ignore("Needs https://github.com/dotnet/dnceng-shared/pull/42")]
    public void AreDependenciesRegistered()
    {
        DependencyInjectionValidation.IsDependencyResolutionCoherent(s =>
            {
                s.AddSingleton<IHostEnvironment>(new HostingEnvironment{EnvironmentName = 
                    Environments.Development});
                s.AddBuildAssetRegistry(options =>
                {
                    options.UseInMemoryDatabase("BuildAssetRegistry");
                    options.EnableServiceProviderCaching(false);
                });
            },
            out string message).Should().BeTrue(message);
    }

}
