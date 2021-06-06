// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Threading.Tasks;
using Microsoft.DotNet.Internal.Testing.DependencyInjection.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Microsoft.DotNet.Internal.Testing.DependencyInjectionCodeGen.Tests
{
    public partial class NoConfigTest
    {
        [TestDependencyInjectionSetup]
        public static class TestDataConfiguration
        {
            public static void Empty(IServiceCollection collection)
            {
            }
        }

        [Test]
        public void ValidateSync()
        {
            using TestData testData = TestData.Default.Build();
        }

        [Test]
        public async Task ValidateAsyncBuild()
        {
            using TestData testData = await TestData.Default.BuildAsync();
        }

        [Test]
        public async Task ValidateAsyncDispose()
        {
            await using TestData testData = TestData.Default.Build();
        }

        [Test]
        public async Task ValidateAsyncAll()
        {
            await using TestData testData = await TestData.Default.BuildAsync();
        }
    }
}
