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
    public partial class MultipleRegistrations
    {
        [TestDependencyInjectionSetup]
        public static class TestDataConfiguration
        {
            public static Func<IServiceProvider, Injectable> Injectable(
                IServiceCollection collection,
                string injectableValue)
            {
                collection.AddSingleton(s => new Injectable(injectableValue));
                return s => s.GetRequiredService<Injectable>();
            }

            [ConfigureAllParameters]
            public static Func<IServiceProvider, ThreeValues> ThreeValues(
                IServiceCollection collection,
                string second,
                string third)
            {
                collection.AddSingleton(s => new ThreeValues(s.GetRequiredService<Injectable>().Value, second, third));
                return s => s.GetRequiredService<ThreeValues>();
            }
        }

        [Test]
        public static void Defaults()
        {
            using TestData testData = TestData.Default.Build();
            testData.Injectable.Value.Should().BeNull();
            testData.ThreeValues.Value1.Should().BeNull();
            testData.ThreeValues.Value2.Should().BeNull();
            testData.ThreeValues.Value3.Should().BeNull();
        }

        [Test]
        public static void SetValues()
        {
            using TestData testData = TestData.Default
                .WithInjectableValue("INJECTABLE-TEST")
                .WithThreeValues("SECOND", "THIRD")
                .Build();
            testData.Injectable.Value.Should().Be("INJECTABLE-TEST");
            testData.ThreeValues.Value1.Should().Be("INJECTABLE-TEST");
            testData.ThreeValues.Value2.Should().Be("SECOND");
            testData.ThreeValues.Value3.Should().Be("THIRD");
        }

        [Test]
        public static void AnyOrder()
        {
            using TestData testData = TestData.Default
                .WithThreeValues("SECOND", "THIRD")
                .WithInjectableValue("INJECTABLE-TEST")
                .Build();
            testData.Injectable.Value.Should().Be("INJECTABLE-TEST");
            testData.ThreeValues.Value1.Should().Be("INJECTABLE-TEST");
            testData.ThreeValues.Value2.Should().Be("SECOND");
            testData.ThreeValues.Value3.Should().Be("THIRD");
        }
    }
}
