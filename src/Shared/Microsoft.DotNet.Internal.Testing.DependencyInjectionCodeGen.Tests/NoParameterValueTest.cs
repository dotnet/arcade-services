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
    public partial class NoParameterValueTest
    {
        private const string TestValue = "TEST-NoParameterValueTest";

        [TestDependencyInjectionSetup]
        private static class TestDataConfig
        {
            public static Func<IServiceProvider, Injectable> Injectable(IServiceCollection collection)
            {
                collection.AddSingleton(s => new Injectable(TestValue));
                return s => s.GetRequiredService<Injectable>();
            }
        }

        [Test]
        public void Validate()
        {
            using TestData testData = TestData.Default.Build();
            testData.Injectable.Value.Should().Be(TestValue);
        }
    }

    //public partial class NoParameterValueTest
    //{
    //    [TestDependencyInjectionSetup]
    //    private static class TestDataConfig
    //    {
    //    }
    //}
}
