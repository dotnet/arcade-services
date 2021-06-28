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

namespace Microsoft.DotNet.Internal.Testing.DependencyInjectionCodeGen.Tests
{
    public partial class SyncNoReturn
    {
        [TestDependencyInjectionSetup]
        private static class TestDataConfig
        {
            public static void Configure(IServiceCollection collection, Ref<bool> called)
            {
                called.Value = true;
            }
        }

        [Test]
        public async Task Validate()
        {
            var called = new Ref<bool>();
            await using TestData testData = await TestData.Default.WithCalled(called).BuildAsync();
            called.Value.Should().BeTrue();
        }
    }

    public partial class SyncWithCallback
    {
        [TestDependencyInjectionSetup]
        private static class TestDataConfig
        {
            public static Action<IServiceProvider> Configure(
                IServiceCollection collection,
                Ref<bool> configCalled,
                Ref<bool> callbackCalled)
            {
                configCalled.Value = true;
                return s => callbackCalled.Value = true;
            }
        }

        [Test]
        public async Task Validate()
        {
            var configCalled = new Ref<bool>();
            var callbackCalled = new Ref<bool>();
            await using TestData testData = await TestData.Default
                .WithConfigCalled(configCalled)
                .WithCallbackCalled(callbackCalled)
                .BuildAsync();
            configCalled.Value.Should().BeTrue();
        }
    }

    public partial class SyncWithReturn
    {
        private const string ValueText = "Test-AsyncWithReturn";

        [TestDependencyInjectionSetup]
        private static class TestDataConfig
        {
            public static Func<IServiceProvider, Injectable> Injectable(IServiceCollection collection)
            {
                return _ => new Injectable(ValueText);
            }
        }

        [Test]
        public async Task Validate()
        {
            await using TestData testData = await TestData.Default
                .BuildAsync();
            testData.Injectable.Value.Should().Be(ValueText);
        }
    }

    public partial class SyncConfigWithAsyncCallback
    {
        [TestDependencyInjectionSetup]
        private static class TestDataConfig
        {
            public static Func<IServiceProvider, Task> Configure(
                IServiceCollection collection,
                Ref<bool> configCalled,
                Ref<bool> callbackCalled)
            {
                configCalled.Value = true;
                return async s => callbackCalled.Value = true;
            }
        }

        [Test]
        public async Task Validate()
        {
            var configCalled = new Ref<bool>();
            var callbackCalled = new Ref<bool>();
            await using TestData testData = await TestData.Default
                .WithConfigCalled(configCalled)
                .WithCallbackCalled(callbackCalled)
                .BuildAsync();
            configCalled.Value.Should().BeTrue();
        }
    }

    public partial class SyncWithAsyncReturn
    {
        private const string ValueText = "Test-DoubleAsyncWithReturn";

        [TestDependencyInjectionSetup]
        private static class TestDataConfig
        {
            public static Func<IServiceProvider, Task<Injectable>> Injectable(IServiceCollection collection)
            {
                return async _ => new Injectable(ValueText);
            }
        }

        [Test]
        public async Task Validate()
        {
            await using TestData testData = await TestData.Default
                .BuildAsync();
            testData.Injectable.Value.Should().Be(ValueText);
        }
    }

    public partial class SyncWithAsyncReturnParameter
    {
        [TestDependencyInjectionSetup]
        private static class TestDataConfig
        {
            public static async Task<Func<IServiceProvider, Task<Injectable>>> Injectable(
                IServiceCollection collection,
                string value)
            {
                return async _ => new Injectable(value);
            }
        }

        [Test]
        public async Task ValidateDefault()
        {
            await using TestData testData = await TestData.Default
                .BuildAsync();
            testData.Injectable.Value.Should().BeNull();
        }

        [Test]
        public async Task ValidateValue()
        {
            const string valueTest = "Test-DoubleAsyncWithReturnParameter";
            await using TestData testData = await TestData.Default
                .WithValue(valueTest)
                .BuildAsync();
            testData.Injectable.Value.Should().Be(valueTest);
        }
    }
}
