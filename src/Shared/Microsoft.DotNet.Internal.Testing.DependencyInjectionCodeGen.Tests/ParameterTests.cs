// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using FluentAssertions;
using Microsoft.DotNet.Internal.Testing.DependencyInjection.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Microsoft.DotNet.Internal.Testing.DependencyInjectionCodeGen.Tests
{
    public class ThreeValues
    {
        public ThreeValues(string value1, string value2, string value3)
        {
            Value1 = value1;
            Value2 = value2;
            Value3 = value3;
        }

        public string Value1 { get; }
        public string Value2 { get; }
        public string Value3 { get; }
    }

    public partial class SeparateParameterTests
    {
        [TestDependencyInjectionSetup]
        private static class TestDataConfiguration
        {
            public static Func<IServiceProvider, ThreeValues> Values(
                IServiceCollection collection,
                string value1,
                string value2,
                string value3)
            {
                return _ => new ThreeValues(value1, value2, value3);
            }
        }

        [Test]
        public void Defaults()
        {
            using TestData testData = TestData.Default.Build();
            testData.Values.Value1.Should().BeNull();
            testData.Values.Value2.Should().BeNull();
            testData.Values.Value3.Should().BeNull();
        }

        [Test]
        public void InOrder()
        {
            using TestData testData = TestData.Default
                .WithValue1("FIRST-VALUE")
                .WithValue2("SECOND-VALUE")
                .WithValue3("THIRD-VALUE")
                .Build();
            testData.Values.Value1.Should().Be("FIRST-VALUE");
            testData.Values.Value2.Should().Be("SECOND-VALUE");
            testData.Values.Value3.Should().Be("THIRD-VALUE");
        }

        [Test]
        public void OutOfOrder()
        {
            using TestData testData = TestData.Default
                .WithValue3("THIRD-VALUE")
                .WithValue1("FIRST-VALUE")
                .WithValue2("SECOND-VALUE")
                .Build();
            testData.Values.Value1.Should().Be("FIRST-VALUE");
            testData.Values.Value2.Should().Be("SECOND-VALUE");
            testData.Values.Value3.Should().Be("THIRD-VALUE");
        }

        [Test]
        public void Partial()
        {
            using TestData testData = TestData.Default
                .WithValue1("FIRST-VALUE")
                .WithValue2("SECOND-VALUE")
                .Build();
            testData.Values.Value1.Should().Be("FIRST-VALUE");
            testData.Values.Value2.Should().Be("SECOND-VALUE");
            testData.Values.Value3.Should().BeNull();
        }
    }

    public partial class CombinedParameterTests
    {
        [TestDependencyInjectionSetup]
        private static class TestDataConfiguration
        {
            [ConfigureAllParameters]
            public static Func<IServiceProvider, ThreeValues> Values(
                IServiceCollection collection,
                string value1,
                string value2,
                string value3)
            {
                return _ => new ThreeValues(value1, value2, value3);
            }
        }

        [Test]
        public void Defaults()
        {
            using TestData testData = TestData.Default.Build();
            testData.Values.Value1.Should().BeNull();
            testData.Values.Value2.Should().BeNull();
            testData.Values.Value3.Should().BeNull();
        }

        [Test]
        public void ConfigureAll()
        {
            using TestData testData = TestData.Default
                .WithValues("FIRST-VALUE", "SECOND-VALUE", "THIRD-VALUE")
                .Build();
            testData.Values.Value1.Should().Be("FIRST-VALUE");
            testData.Values.Value2.Should().Be("SECOND-VALUE");
            testData.Values.Value3.Should().Be("THIRD-VALUE");
        }
    }
}
