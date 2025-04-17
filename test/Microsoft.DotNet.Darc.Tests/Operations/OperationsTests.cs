// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Threading.Tasks;
using Shouldly;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.DotNet.Darc.Options;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Microsoft.DotNet.Darc.Tests.Operations;

[TestFixture]
public class OperationTests
{
    public const DarcOutputType YmlDarcOutputType = (DarcOutputType)0xFF;
    [Test]
    public void OperationTests_IsOutputFormatSupported_default_should_not_throw()
    {
        GetBuildCommandLineOptions options = new();
        options.OutputFormat.ShouldBe(DarcOutputType.text);

        // If we got this far - all good
        options.ShouldNotBeNull();
    }

    [Test]
    public void OperationTests_IsOutputFormatSupported_should_throw_if_outputFormat_not_supported()
    {

        ((Action)(() => _ = new FakeCommandLineOptions { OutputFormat = YmlDarcOutputType })).ShouldThrow<ArgumentException>();
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
        options.ShouldNotBeNull();
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
