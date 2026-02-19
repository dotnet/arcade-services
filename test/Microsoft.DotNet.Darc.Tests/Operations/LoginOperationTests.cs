// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using AwesomeAssertions;
using Microsoft.DotNet.ProductConstructionService.Client;
using NUnit.Framework;

namespace Microsoft.DotNet.Darc.Tests.Operations;

[TestFixture]
public class LoginOperationTests
{
    [Test]
    public void GetAppIdForUri_ProductionMaestro()
    {
        // Test that production Maestro URI is correctly mapped to production app ID
        var appId = ProductConstructionServiceApiOptions.GetAppIdForUri(ProductConstructionServiceApiOptions.ProductionMaestroUri);
        appId.Should().Be("54c17f3d-7325-4eca-9db7-f090bfc765a8");
    }

    [Test]
    public void GetAppIdForUri_OldProductionMaestro()
    {
        // Test that old production Maestro URI is correctly mapped to production app ID
        var appId = ProductConstructionServiceApiOptions.GetAppIdForUri(ProductConstructionServiceApiOptions.OldProductionMaestroUri);
        appId.Should().Be("54c17f3d-7325-4eca-9db7-f090bfc765a8");
    }

    [Test]
    public void GetAppIdForUri_StagingMaestro()
    {
        // Test that staging Maestro URI is correctly mapped to staging app ID
        var appId = ProductConstructionServiceApiOptions.GetAppIdForUri(ProductConstructionServiceApiOptions.StagingMaestroUri);
        appId.Should().Be("baf98f1b-374e-487d-af42-aa33807f11e4");
    }

    [Test]
    public void GetAppIdForUri_OldStagingMaestro()
    {
        // Test that old staging Maestro URI is correctly mapped to staging app ID
        var appId = ProductConstructionServiceApiOptions.GetAppIdForUri(ProductConstructionServiceApiOptions.OldStagingMaestroUri);
        appId.Should().Be("baf98f1b-374e-487d-af42-aa33807f11e4");
    }

    [Test]
    public void GetAppIdForUri_LocalMaestro()
    {
        // Test that local Maestro URI is correctly mapped to staging app ID
        var appId = ProductConstructionServiceApiOptions.GetAppIdForUri(ProductConstructionServiceApiOptions.PcsLocalUri);
        appId.Should().Be("baf98f1b-374e-487d-af42-aa33807f11e4");
    }

    [Test]
    public void GetAppIdForUri_WithTrailingSlash()
    {
        // Test that URIs with trailing slashes are handled correctly
        var appId = ProductConstructionServiceApiOptions.GetAppIdForUri("https://maestro.dot.net/");
        appId.Should().Be("54c17f3d-7325-4eca-9db7-f090bfc765a8");
    }

    [Test]
    public void GetAppIdForUri_WithoutTrailingSlash()
    {
        // Test that URIs without trailing slashes are handled correctly
        var appId = ProductConstructionServiceApiOptions.GetAppIdForUri("https://maestro.dot.net");
        appId.Should().Be("54c17f3d-7325-4eca-9db7-f090bfc765a8");
    }

    [Test]
    public void GetAppIdForUri_UnknownUri_ThrowsException()
    {
        // Test that unknown URIs throw an appropriate exception
        Action action = () => ProductConstructionServiceApiOptions.GetAppIdForUri("https://unknown.maestro.com/");
        action.Should().Throw<ArgumentException>()
            .WithMessage("*Unknown Maestro URI*");
    }
}
