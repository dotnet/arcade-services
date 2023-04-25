// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DotNet.Internal.Testing.DependencyInjection.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Microsoft.DotNet.Internal.Testing.DependencyInjectionCodeGen.Tests;

public partial class DisposeCalledTest
{
    [TestDependencyInjectionSetup]
    private static class TestDataConfiguration
    {
        public static Func<IServiceProvider, Injectable> Injectable(IServiceCollection collection)
        {
            collection.AddSingleton(s => new Injectable("TEST-VALUE"));
            return s => s.GetRequiredService<Injectable>();
        }
    }

    [Test]
    public void ValidateSync()
    {
        TestData testData;
        using (testData = TestData.Default.Build())
        {
            testData.Injectable.IsSyncDisposeCalled.Should().BeFalse();
            testData.Injectable.IsAsyncDisposeCalled.Should().BeFalse();
        }

        testData.Injectable.IsSyncDisposeCalled.Should().BeTrue();
        testData.Injectable.IsAsyncDisposeCalled.Should().BeFalse();
    }

    [Test]
    public async Task ValidateAsync()
    {
        TestData testData;
        await using (testData = await TestData.Default.BuildAsync())
        {
            testData.Injectable.IsSyncDisposeCalled.Should().BeFalse();
            testData.Injectable.IsAsyncDisposeCalled.Should().BeFalse();
        }

        testData.Injectable.IsSyncDisposeCalled.Should().BeFalse();
        testData.Injectable.IsAsyncDisposeCalled.Should().BeTrue();
    }
}
