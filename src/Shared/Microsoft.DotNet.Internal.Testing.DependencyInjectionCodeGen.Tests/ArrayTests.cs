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
    public partial class ArrayTests
    {
        [TestDependencyInjectionSetup]
        public static class TestDataConfiguration
        {
            public static Func<IServiceProvider, string[]> Values(
                IServiceCollection collection,
                string[] values)
            {
                return _ => values;
            }
        }

        [Test]
        public void Defaults()
        {
            using TestData testData = TestData.Default.Build();
            testData.Values.Should().BeNull();
        }
        
        [Test]
        public void WithValue()
        {
            using TestData testData = TestData
                .Default
                .WithValues(new[] { "pizza", "banana" })
                .Build();
            testData.Values.Should().NotBeNull()
                .And.BeEquivalentTo("pizza", "banana");
        }
    }
}
