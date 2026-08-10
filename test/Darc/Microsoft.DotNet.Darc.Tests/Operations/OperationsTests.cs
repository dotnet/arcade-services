// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using System;

namespace Microsoft.DotNet.Darc.Tests.Operations;

[TestFixture]
public class OperationTests
{
    public const DarcOutputType YmlDarcOutputType = (DarcOutputType)0xFF;
    [Test]
    public void OperationTests_IsOutputFormatSupported_default_should_not_throw()
    {
        GetBuildCommandLineOptions options = new();
        options.OutputFormat.Should().Be(DarcOutputType.text);

        // If we got this far - all good
        options.Should().NotBeNull();
    }

    [Test]
    public void OperationTests_IsOutputFormatSupported_should_throw_if_outputFormat_not_supported()
    {

        ((Action)(() => _ = new FakeCommandLineOptions { OutputFormat = YmlDarcOutputType })).Should()
            .Throw<ArgumentException>();
    }

    [TestCase(DarcOutputType.text)]
    [TestCase(DarcOutputType.json)]
    public void OperationTests_IsOutputFormatSupported_should_not_throw_if_outputFormat_supported(DarcOutputType outputFormat)
    {
        FakeCommandLineOptions options = new()
        {
            OutputFormat = outputFormat,
        };

        // If we got this far - all good
        options.Should().NotBeNull();
    }

    [TestCase(ProductConstructionServiceApiOptions.ProductionMaestroUri)]
    [TestCase("https://maestro.dot.net")]
    public void InitializeFromSettings_DoesNotWarnForDefaultMaestroUri(string maestroUri)
    {
        Mock<ILogger> logger = new();
        FakeCommandLineOptions options = new()
        {
            BuildAssetRegistryBaseUri = maestroUri,
        };

        options.InitializeFromSettings(logger.Object);

        logger.Verify(
            log => log.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Never);
    }

    [TestCase(ProductConstructionServiceApiOptions.OldProductionMaestroUri, ProductConstructionServiceApiOptions.ProductionMaestroUri)]
    [TestCase(ProductConstructionServiceApiOptions.OldStagingMaestroUri, ProductConstructionServiceApiOptions.StagingMaestroUri)]
    public void InitializeFromSettings_WarnsForOutdatedMaestroUri(string maestroUri, string replacementUri)
    {
        Mock<ILogger> logger = new();
        FakeCommandLineOptions options = new()
        {
            BuildAssetRegistryBaseUri = maestroUri,
        };

        options.InitializeFromSettings(logger.Object);

        VerifyWarningContains(logger, maestroUri, "outdated", replacementUri);
    }

    [Test]
    public void InitializeFromSettings_WarnsForNonDefaultMaestroUri()
    {
        Mock<ILogger> logger = new();
        FakeCommandLineOptions options = new()
        {
            BuildAssetRegistryBaseUri = ProductConstructionServiceApiOptions.StagingMaestroUri,
        };

        options.InitializeFromSettings(logger.Object);

        VerifyWarningContains(logger, ProductConstructionServiceApiOptions.StagingMaestroUri, "non-default");
    }

    private static void VerifyWarningContains(Mock<ILogger> logger, params string[] expectedValues)
    {
        logger.Verify(
            log => log.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => Array.TrueForAll(expectedValues, value => state.ToString().Contains(value))),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    public class FakeCommandLineOptions : CommandLineOptions
    {
        public override Operation GetOperation(ServiceProvider sp) => throw new NotImplementedException();
        public override bool IsOutputFormatSupported()
            => OutputFormat switch
            {
                DarcOutputType.json => true,
                _ => base.IsOutputFormatSupported(),
            };
    }
}
