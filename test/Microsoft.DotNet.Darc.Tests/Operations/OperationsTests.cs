// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Microsoft.DotNet.Darc.Options;
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

        ((Action)(() => _ = new JsonCommandLineOptions { OutputFormat = YmlDarcOutputType })).Should()
            .Throw<ArgumentException>();
    }

    [TestCase(DarcOutputType.text)]
    [TestCase(DarcOutputType.json)]
    public void OperationTests_IsOutputFormatSupported_should_not_throw_if_outputFormat_supported(DarcOutputType outputFormat)
    {
        JsonCommandLineOptions options = new()
        {
            OutputFormat = outputFormat,
        };

        // If we got this far - all good
        options.Should().NotBeNull();
    }

    private class JsonCommandLineOptions : CommandLineOptions
    {
        public override Type GetOperation() => throw new NotImplementedException();
        public override bool IsOutputFormatSupported()
            => OutputFormat switch
            {
                DarcOutputType.json => true,
                _ => base.IsOutputFormatSupported(),
            };
    }
}
