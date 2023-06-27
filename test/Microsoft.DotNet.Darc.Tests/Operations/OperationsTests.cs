// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.DotNet.Darc.Options;
using NUnit.Framework;
using System;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Darc.Tests.Operations;

[TestFixture]
public class OperationTests
{
    [Test]
    public void OperationTests_IsOutputFormatSupported_default_should_not_throw()
    {
        MockCommandLineOptions options = new();
        options.OutputFormat.Should().Be(DarcOutputType.text);

        TextOutputOperation operation = new(options);

        // If we got this far - all good
        operation.Should().NotBeNull();
    }

    [Test]
    public void OperationTests_IsOutputFormatSupported_should_throw_if_outputFormat_not_supported()
    {
        MockCommandLineOptions options = new()
        {
            OutputFormat = DarcOutputType.json,
        };

        ((Action)(() => _ = new TextOutputOperation(options))).Should()
            .Throw<NotImplementedException>()
            .WithMessage("Output format type 'json' not yet supported for this operation.\r\nPlease raise a new issue in https://github.com/dotnet/arcade/issues/.");
    }

    [TestCase(DarcOutputType.text)]
    [TestCase(DarcOutputType.json)]
    [TestCase(YmlJsonOutputOperation.YmlDarcOutputType)]
    public void OperationTests_IsOutputFormatSupported_should_not_throw_if_outputFormat_supported(DarcOutputType outputFormat)
    {
        MockCommandLineOptions options = new()
        {
            OutputFormat = outputFormat,
        };

        YmlJsonOutputOperation operation = new(options);

        // If we got this far - all good
        operation.Should().NotBeNull();
    }

    private class TextOutputOperation : Operation
    {
        public TextOutputOperation(CommandLineOptions options)
            : base(options)
        {
        }

        public override Task<int> ExecuteAsync() => throw new NotImplementedException();
    }

    private class JsonOutputOperation : Operation
    {
        public JsonOutputOperation(CommandLineOptions options)
            : base(options)
        {
        }

        public override Task<int> ExecuteAsync() => throw new NotImplementedException();

        protected override bool IsOutputFormatSupported(DarcOutputType outputFormat)
            => outputFormat switch
            {
                DarcOutputType.json => true,
                _ => base.IsOutputFormatSupported(outputFormat),
            };
    }

    private class YmlJsonOutputOperation : JsonOutputOperation
    {
        public const DarcOutputType YmlDarcOutputType = (DarcOutputType)0xFF;

        public YmlJsonOutputOperation(CommandLineOptions options)
            : base(options)
        {
        }

        public override Task<int> ExecuteAsync() => throw new NotImplementedException();

        protected override bool IsOutputFormatSupported(DarcOutputType outputFormat)
            => outputFormat switch
            {
                YmlDarcOutputType => true,
                _ => base.IsOutputFormatSupported(outputFormat),
            };
    }

    private class MockCommandLineOptions : CommandLineOptions
    {
        public override Operation GetOperation() => throw new NotImplementedException();
    }
}
