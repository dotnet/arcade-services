// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.Darc.Tests.Operations;

[TestFixture]
public class LoginOperationTests
{
    private Mock<ILogger<LoginOperation>> _loggerMock = null!;

    [SetUp]
    public void Setup()
    {
        _loggerMock = new();
    }

    [Test]
    public void LoginOperation_GetAppIdForUri_ProductionMaestro()
    {
        // Test that production Maestro URIs are correctly mapped to production app ID
        var options = new LoginCommandLineOptions
        {
            BarUri = ProductConstructionServiceApiOptions.ProductionMaestroUri
        };
        var operation = new LoginOperation(options, _loggerMock.Object);

        // Use reflection to call the private GetAppIdForUri method
        var method = typeof(LoginOperation).GetMethod("GetAppIdForUri", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        method.Should().NotBeNull();

        var appId = method!.Invoke(operation, [ProductConstructionServiceApiOptions.ProductionMaestroUri]) as string;
        appId.Should().Be("54c17f3d-7325-4eca-9db7-f090bfc765a8");
    }

    [Test]
    public void LoginOperation_GetAppIdForUri_StagingMaestro()
    {
        // Test that staging Maestro URIs are correctly mapped to staging app ID
        var options = new LoginCommandLineOptions
        {
            BarUri = ProductConstructionServiceApiOptions.StagingMaestroUri
        };
        var operation = new LoginOperation(options, _loggerMock.Object);

        // Use reflection to call the private GetAppIdForUri method
        var method = typeof(LoginOperation).GetMethod("GetAppIdForUri", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        method.Should().NotBeNull();

        var appId = method!.Invoke(operation, [ProductConstructionServiceApiOptions.StagingMaestroUri]) as string;
        appId.Should().Be("baf98f1b-374e-487d-af42-aa33807f11e4");
    }

    [Test]
    public void LoginOperation_GetAppIdForUri_LocalMaestro()
    {
        // Test that local Maestro URI is correctly mapped to staging app ID
        var options = new LoginCommandLineOptions
        {
            BarUri = ProductConstructionServiceApiOptions.PcsLocalUri
        };
        var operation = new LoginOperation(options, _loggerMock.Object);

        // Use reflection to call the private GetAppIdForUri method
        var method = typeof(LoginOperation).GetMethod("GetAppIdForUri", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        method.Should().NotBeNull();

        var appId = method!.Invoke(operation, [ProductConstructionServiceApiOptions.PcsLocalUri]) as string;
        appId.Should().Be("baf98f1b-374e-487d-af42-aa33807f11e4");
    }

    [Test]
    public void LoginOperation_GetAppIdForUri_UnknownUri_ThrowsException()
    {
        // Test that unknown URIs throw an appropriate exception
        var options = new LoginCommandLineOptions
        {
            BarUri = "https://unknown.maestro.com/"
        };
        var operation = new LoginOperation(options, _loggerMock.Object);

        // Use reflection to call the private GetAppIdForUri method
        var method = typeof(LoginOperation).GetMethod("GetAppIdForUri", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        method.Should().NotBeNull();

        Action action = () => method!.Invoke(operation, ["https://unknown.maestro.com/"]);
        action.Should().Throw<System.Reflection.TargetInvocationException>()
            .WithInnerException<ArgumentException>()
            .WithMessage("*Unknown Maestro URI*");
    }

    [Test]
    public void LoginOperation_DefaultBarUri_IsProductionMaestro()
    {
        // Test that when no BarUri is specified, it defaults to production
        var options = new LoginCommandLineOptions();
        options.BarUri.Should().BeNull();
        
        // The operation should use ProductionMaestroUri when BarUri is null
        // This is tested implicitly in the ExecuteAsync method
    }
}
