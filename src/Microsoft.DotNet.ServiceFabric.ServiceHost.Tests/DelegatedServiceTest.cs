// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using FluentAssertions.Execution;
using FluentAssertions.Specialized;
using Microsoft.DotNet.Internal.Testing.DependencyInjection.Abstractions;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost.Tests;

[Serializable]
public class TestThrowableException : Exception
{
    public TestThrowableException()
    {
    }

    public TestThrowableException(string message)
        : base(message)
    {
    }

    public TestThrowableException(string message, Exception inner)
        : base(message, inner)
    {
    }

    protected TestThrowableException(
        SerializationInfo info,
        StreamingContext context)
        : base(info, context)
    {
    }
}

public partial class DelegatedServiceTest
{
    [TestDependencyInjectionSetup]
    private static class TestDataConfig
    {
        public static Func<IServiceProvider, CancellationTokenSource> CancellationToken(IServiceCollection collection)
        {
            CancellationTokenSource source = new CancellationTokenSource();
            return _ => source;
        }

        public static Func<IServiceProvider, IServiceProvider> ServiceProvider(IServiceCollection collection) =>
            s => s;

        public static Func<IServiceProvider, CountingLogger> Logger(IServiceCollection collection)
        {
            CountingLogger logger = new CountingLogger();
            collection.AddLogging(
                l => l.AddProvider(logger));
            return _ => logger;
        }

        public static Func<IServiceProvider, Dictionary<string, int>> Counter(IServiceCollection collection)
        {
            Dictionary<string, int> counter = new Dictionary<string, int>();
            return _ => counter;
        }
    }

    [Test]
    public async Task SingleCancellationExitsAsExpected()
    {
        TestData testData = await TestData.Default.BuildAsync();
        Task task = Run(testData, WaitLoop);
        testData.CancellationToken.Cancel();
        Func<Task> callback = () => task;
        (await callback.Should().ThrowAsync<OperationCanceledException>())
            .Where(e => e.CancellationToken == testData.CancellationToken.Token, "cancellation token should match");
    }

    [Test]
    public async Task AbnormalExitIsWarned()
    {
        TestData testData = await TestData.Default.BuildAsync();
        Task task = Run(testData, ExitsWithoutWaitingLoop);
        Func<Task> callback = () => task;
        await callback.Should().CompleteWithinAsync(TimeSpan.FromSeconds(1));
        testData.Logger.Warning.Should().Be(1);
    }

    [Test]
    public async Task ExceptionExitIsLogged()
    {
        TestData testData = await TestData.Default.BuildAsync();
        Task task = Run(testData, ThrowsTestException);
        Func<Task> callback = () => task;
        await callback.Should().ThrowAsync<TestThrowableException>();
        testData.Logger.Exception.Should().Be(1);
    }

    [Test]
    public async Task MultipleCancellationExitsAsExpected()
    {
        TestData testData = await TestData.Default.BuildAsync();
        Task task = Run(testData, WaitLoop, WaitLoop);
        testData.CancellationToken.Cancel();
        Func<Task> callback = () => task;
        (await callback.Should().ThrowAsync<OperationCanceledException>())
            .Where(e => e.CancellationToken == testData.CancellationToken.Token, "cancellation token should match");
        AssertionExtensions.Should(testData.Counter).ContainKey(nameof(WaitLoop))
            .WhichValue.Should().Be(2, "both loops should have executed");
        testData.Logger.Error.Should().Be(0);
        testData.Logger.Warning.Should().Be(0);
    }

    [Test]
    public async Task WaitDelaysUntilCancellation()
    {
        TestData testData = await TestData.Default.BuildAsync();
        Task task = Run(testData, WaitLoop);
        Func<Task> callback = () => task;
        await callback.Should().NotCompleteWithinAsync(TimeSpan.FromSeconds(1));
        testData.CancellationToken.Cancel();
        (await callback.Should().ThrowAsync<OperationCanceledException>())
            .Where(e => e.CancellationToken == testData.CancellationToken.Token, "cancellation token should match");
        testData.Logger.Error.Should().Be(0);
        testData.Logger.Warning.Should().Be(0);
    }
        
    [Test]
    public async Task AbnormalExitCausesCancellation()
    {
        TestData testData = await TestData.Default.BuildAsync();
        Task task = Run(testData, ExitsWithoutWaitingLoop, WaitLoop);
        Func<Task> callback = () => task;
        await callback.Should().CompleteWithinAsync(TimeSpan.FromSeconds(1), because: "First task exiting should cancel the others");
        testData.Logger.Warning.Should().Be(1);
    }
        
    [Test]
    public async Task ThrowExceptionCausesCancellation()
    {
        TestData testData = await TestData.Default.BuildAsync();
        Task task = Run(testData, ThrowsTestException, WaitLoop);
        Func<Task> callback = () => task;
        await callback.Should().ThrowAsync<TestThrowableException>();
        testData.Logger.Exception.Should().Be(1);
    }
        
    [Test]
    public async Task DoubleThrowLogsBoth()
    {
        TestData testData = await TestData.Default.BuildAsync();
        Task task = Run(testData, ThrowsTestException, ThrowsTestException);
        Func<Task> callback = () => task;
        await callback.Should().ThrowAsync<TestThrowableException>();
        testData.Logger.Exception.Should().Be(2);
    }

    private static Task Run(TestData testData, params Func<TestData, CancellationToken, Task>[] loops)
    {
        return DelegatedService.RunServiceLoops<DelegatedServiceTest>(
            testData.ServiceProvider,
            testData.CancellationToken.Token,
            loops.Select(loop => (Func<CancellationToken, Task>)(t => loop(testData, t))).ToArray()
        );
    }

    private static Task ExitsWithoutWaitingLoop(TestData testData, CancellationToken ignored)
    {
        CountCall(testData);
        return Task.CompletedTask;
    }

    private static Task ThrowsTestException(TestData testData, CancellationToken ignored)
    {
        CountCall(testData);
        return Task.FromException(new TestThrowableException("TEST-EXCEPTION"));
    }

    private static Task WaitLoop(TestData testData, CancellationToken token)
    {
        CountCall(testData);
        return token.AsTask();
    }

    private static void CountCall(TestData data, [CallerMemberName] string key = "Unknown")
    {
        var count = data.Counter;
        lock (count)
        {
            if (count.TryGetValue(key, out var value))
            {
                count[key] = value + 1;
            }
            else
            {
                count.Add(key, 1);
            }
        }
    }
}
    
public static class NotCompleteGenericAsyncTaskAssertion
{
    public static async Task<AndConstraint<AsyncFunctionAssertions>> NotCompleteWithinAsync(
        this NonGenericAsyncFunctionAssertions parentConstraint,
        TimeSpan timeSpan,
        string because = "",
        params object[] becauseArgs)
    {
        Execute.Assertion.ForCondition(parentConstraint.Subject != null)
            .BecauseOf(because, becauseArgs)
            .FailWith(
                "Expected {context:task} to complete within {0}{reason}, but found <null>.",
                new object[1]
                    { timeSpan }
            );
        using var timeoutCancellationTokenSource = new CancellationTokenSource();
        Task task = parentConstraint.Subject();
        Task completedTask = await Task.WhenAny(task, Task.Delay(timeSpan, timeoutCancellationTokenSource.Token)).ConfigureAwait(false);
        if (completedTask == task)
        {
            timeoutCancellationTokenSource.Cancel();
            await completedTask.ConfigureAwait(false);
        }
        Execute.Assertion.ForCondition(completedTask != task).BecauseOf(because, becauseArgs).FailWith("Expected {context:task} not to complete within {0}{reason}.", new object[1]
        {
            timeSpan
        });
        var andConstraint = new AndConstraint<AsyncFunctionAssertions>(parentConstraint);
        return andConstraint;
    }
}
