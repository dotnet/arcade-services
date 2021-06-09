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
    public partial class WithParameterValueTest
    {
        [TestDependencyInjectionSetup]
        private static class TestDataConfig
        {
            public static Func<IServiceProvider, Injectable> Injectable(IServiceCollection collection, string value)
            {
                collection.AddSingleton(s => new Injectable(value));
                return s => s.GetRequiredService<Injectable>();
            }
        }

        [Test]
        public void ValidateDefault()
        {
            using TestData testData = TestData.Default.Build();
            testData.Injectable.Value.Should().BeNull();
        }

        [Test]
        public void ValidateValue()
        {
            const string testValue = "TEST-WithParameterValueTest";
            using TestData testData = TestData.Default.WithValue(testValue).Build();
            testData.Injectable.Value.Should().Be(testValue);
        }
    }
}
