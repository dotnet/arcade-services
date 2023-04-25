// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DotNet.Internal.Testing.DependencyInjection.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

#pragma warning disable 1998

namespace Microsoft.DotNet.Internal.Testing.DependencyInjectionCodeGen.Tests;

public partial class SyncTupleTest
{
    [TestDependencyInjectionSetup]
    public static class TestDataConfiguration
    {
        public static Func<IServiceProvider, (string StringValue, int IntValue)> Values(
            IServiceCollection collection,
            string stringValue,
            int intValue)
        {
            return _ => (stringValue, intValue);
        }
    }

    [Test]
    public void Defaults()
    {
        using TestData testData = TestData.Default.Build();
        testData.Values.StringValue.Should().BeNull();
        testData.Values.IntValue.Should().Be(0);
    }

    [Test]
    public void ConfigureAll()
    {
        using TestData testData = TestData.Default
            .WithStringValue("FIRST-VALUE")
            .WithIntValue(98765)
            .Build();
        testData.Values.StringValue.Should().Be("FIRST-VALUE");
        testData.Values.IntValue.Should().Be(98765);
    }
}

public partial class AsyncReturnTupleTest
{
    [TestDependencyInjectionSetup]
    public static class TestDataConfiguration
    {
        public static Func<IServiceProvider, Task<(string StringValue, int IntValue)>> Values(
            IServiceCollection collection,
            string stringValue,
            int intValue)
        {
            return async _ => (stringValue, intValue);
        }
    }

    [Test]
    public void Defaults()
    {
        using TestData testData = TestData.Default.Build();
        testData.Values.StringValue.Should().BeNull();
        testData.Values.IntValue.Should().Be(0);
    }

    [Test]
    public void ConfigureAll()
    {
        using TestData testData = TestData.Default
            .WithStringValue("FIRST-VALUE")
            .WithIntValue(98765)
            .Build();
        testData.Values.StringValue.Should().Be("FIRST-VALUE");
        testData.Values.IntValue.Should().Be(98765);
    }
}

public partial class AsyncConfigurationTupleTest
{
    [TestDependencyInjectionSetup]
    public static class TestDataConfiguration
    {
        public static async Task<Func<IServiceProvider, (string StringValue, int IntValue)>> Values(
            IServiceCollection collection,
            string stringValue,
            int intValue)
        {
            return _ => (stringValue, intValue);
        }
    }

    [Test]
    public void Defaults()
    {
        using TestData testData = TestData.Default.Build();
        testData.Values.StringValue.Should().BeNull();
        testData.Values.IntValue.Should().Be(0);
    }

    [Test]
    public void ConfigureAll()
    {
        using TestData testData = TestData.Default
            .WithStringValue("FIRST-VALUE")
            .WithIntValue(98765)
            .Build();
        testData.Values.StringValue.Should().Be("FIRST-VALUE");
        testData.Values.IntValue.Should().Be(98765);
    }
}

public partial class DoubleAsyncTupleTest
{
    [TestDependencyInjectionSetup]
    public static class TestDataConfiguration
    {
        public static async Task<Func<IServiceProvider, Task<(string StringValue, int IntValue)>>> Values(
            IServiceCollection collection,
            string stringValue,
            int intValue)
        {
            return async _ => (stringValue, intValue);
        }
    }

    [Test]
    public void Defaults()
    {
        using TestData testData = TestData.Default.Build();
        testData.Values.StringValue.Should().BeNull();
        testData.Values.IntValue.Should().Be(0);
    }

    [Test]
    public void ConfigureAll()
    {
        using TestData testData = TestData.Default
            .WithStringValue("FIRST-VALUE")
            .WithIntValue(98765)
            .Build();
        testData.Values.StringValue.Should().Be("FIRST-VALUE");
        testData.Values.IntValue.Should().Be(98765);
    }
}
