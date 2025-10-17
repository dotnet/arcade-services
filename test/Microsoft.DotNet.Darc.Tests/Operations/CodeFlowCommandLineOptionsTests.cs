// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System;

namespace Microsoft.DotNet.Darc.Tests.Operations;

[TestFixture]
public class CodeFlowCommandLineOptionsTests
{
    [Test]
    public void RegisterServices_should_throw_when_both_build_and_ref_are_specified()
    {
        // Arrange
        var options = new TestCodeFlowCommandLineOptions
        {
            Build = 123,
            Ref = "main"
        };

        // Act & Assert
        ((Action)(() => options.RegisterServices(new ServiceCollection()))).Should()
            .Throw<ArgumentException>()
            .WithMessage("*--build*--ref*cannot be used together*");
    }

    [Test]
    public void RegisterServices_should_not_throw_when_only_build_is_specified()
    {
        // Arrange
        var options = new TestCodeFlowCommandLineOptions
        {
            Build = 123,
            Ref = null
        };

        // Act & Assert
        options.Invoking(o => o.RegisterServices(new ServiceCollection())).Should().NotThrow<ArgumentException>();
    }

    [Test]
    public void RegisterServices_should_not_throw_when_only_ref_is_specified()
    {
        // Arrange
        var options = new TestCodeFlowCommandLineOptions
        {
            Build = 0,
            Ref = "main"
        };

        // Act & Assert
        options.Invoking(o => o.RegisterServices(new ServiceCollection())).Should().NotThrow<ArgumentException>();
    }

    [Test]
    public void RegisterServices_should_not_throw_when_neither_build_nor_ref_are_specified()
    {
        // Arrange
        var options = new TestCodeFlowCommandLineOptions
        {
            Build = 0,
            Ref = null
        };

        // Act & Assert
        options.Invoking(o => o.RegisterServices(new ServiceCollection())).Should().NotThrow<ArgumentException>();
    }

    private class TestCodeFlowCommandLineOptions : ForwardFlowCommandLineOptions
    {
        // ForwardFlowCommandLineOptions is not abstract, so we can use it directly for testing
    }
}
