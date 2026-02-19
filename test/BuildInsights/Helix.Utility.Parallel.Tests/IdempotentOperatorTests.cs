using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using AwesomeAssertions.Extensions;
using NUnit.Framework;

namespace Microsoft.Internal.Helix.Utility.Parallel.Tests
{
    [TestFixture]
    public class IdempotentOperatorTests
    {
        [Test]
        public async Task SimpleExecution()
        {
            int count = 0;

            var executor = new IdempotentOperator<string, string, int, string>((ctx, amount, context) =>
            {
                var newCount = Interlocked.Add(ref count, amount);
                return Task.FromResult($"{ctx}-{newCount}");
            }, TimeSpan.FromHours(1));

            count.Should().Be(0);
            var result = await executor.ExecuteAsync("A", "Result", 5);
            count.Should().Be(5);
            result.Should().Be("Result-5");
        }

        [Test]
        public async Task Idempotent()
        {
            int count = 0;

            var executor = new IdempotentOperator<string, string, int, string>((ctx, amount, context) =>
            {
                var newCount = Interlocked.Add(ref count, amount);
                return Task.FromResult($"{ctx}-{newCount}");
            }, TimeSpan.FromHours(1));

            count.Should().Be(0);
            var result = await executor.ExecuteAsync("A", "Result", 5);
            count.Should().Be(5);
            result.Should().Be("Result-5");
            result = await executor.ExecuteAsync("A", "Result", 5);
            count.Should().Be(5);
            result.Should().Be("Result-5");
        }

        [Test]
        public async Task DifferentKeys()
        {
            int count = 0;

            var executor = new IdempotentOperator<string, string, int, string>((ctx, amount, context) =>
            {
                var newCount = Interlocked.Add(ref count, amount);
                return Task.FromResult($"{ctx}-{newCount}");
            }, TimeSpan.FromHours(1));

            count.Should().Be(0);
            var result = await executor.ExecuteAsync("A", "Result", 5);
            count.Should().Be(5);
            result.Should().Be("Result-5");
            result = await executor.ExecuteAsync("B", "Result", 5);
            count.Should().Be(10);
            result.Should().Be("Result-10");
        }

        [Test]
        public async Task TimeElapsed()
        {
            int count = 0;

            var executor = new IdempotentOperator<string, string, int, string>((ctx, amount, context) =>
            {
                var newCount = Interlocked.Add(ref count, amount);
                return Task.FromResult($"{ctx}-{newCount}");
            }, TimeSpan.FromMilliseconds(1));

            count.Should().Be(0);
            var result = await executor.ExecuteAsync("A", "Result", 5);
            count.Should().Be(5);
            result.Should().Be("Result-5");
            await Task.Delay(TimeSpan.FromMilliseconds(5));
            result = await executor.ExecuteAsync("A", "Result", 5);
            count.Should().Be(10);
            result.Should().Be("Result-10");
        }

        [Test]
        public async Task ExceptionRetries()
        {
            int count = 0;

            var executor = new IdempotentOperator<string, string, int, string>((ctx, amount, context) =>
            {
                var newCount = Interlocked.Add(ref count, amount);
                throw new Exception($"{ctx}-{newCount}");
            }, TimeSpan.FromHours(1));

            count.Should().Be(0);
            Exception e = (await (((Func<Task>)(() => executor.ExecuteAsync("A", "Result", 5)))).Should().ThrowAsync<Exception>()).Which;
            count.Should().Be(5);
            e.Message.Should().Be("Result-5");
            e = (await (((Func<Task>)(() => executor.ExecuteAsync("A", "Result", 5)))).Should().ThrowAsync<Exception>()).Which;
            count.Should().Be(10);
            e.Message.Should().Be("Result-10");
        }

        [Test]
        public async Task ParallelIdempotent()
        {
            int count = 0;

            var executor = new IdempotentOperator<string, Task<string>, int, string>(async (ctx, amount, context) =>
            {
                await ctx;
                var newCount = Interlocked.Add(ref count, amount);
                return $"{await ctx}-{newCount}";
            }, TimeSpan.FromHours(1));

            TaskCompletionSource<string> waiting = new TaskCompletionSource<string>();
            TaskCompletionSource<string> done = new TaskCompletionSource<string>();
            done.SetResult("INCORRECT");

            Task<string> first = executor.ExecuteAsync("A", waiting.Task, 5);
            Task<string> second = executor.ExecuteAsync("A", done.Task, 5);

            first.IsCompleted.Should().BeFalse();
            second.IsCompleted.Should().BeFalse();

            count.Should().Be(0);
            waiting.SetResult("Result");

            var result = await first;
            count.Should().Be(5);
            result.Should().Be("Result-5");

            result = await second;
            count.Should().Be(5);
            result.Should().Be("Result-5");
        }

        [Test]
        public async Task ParallelThrow()
        {
            int count = 0;

            var executor = new IdempotentOperator<string, Task<string>, int, string>(async (ctx, amount, context) =>
            {
                var newCount = Interlocked.Add(ref count, amount);
                await ctx;
                return $"{await ctx}-{newCount}";
            }, TimeSpan.FromHours(1));

            TaskCompletionSource<string> waiting = new TaskCompletionSource<string>();
            TaskCompletionSource<string> done = new TaskCompletionSource<string>();
            done.SetResult("Result");

            count.Should().Be(0);

            Task<string> first = executor.ExecuteAsync("A", waiting.Task, 5);
            Task<string> second = executor.ExecuteAsync("A", done.Task, 5);

            first.IsCompleted.Should().BeFalse();
            second.IsCompleted.Should().BeFalse();
            waiting.SetException(new Exception("Expected"));

            var e = (await (((Func<Task>)(() => first))).Should().ThrowAsync<Exception>()).Which;
            e.Message.Should().Be("Expected");

            var result = await second;
            count.Should().Be(10);
            result.Should().Be("Result-10");
        }

        [Test]
        public async Task RejectedResultNotCached()
        {
            int count = 0;

            var executor = new IdempotentOperator<string, Task, int, string>(async (ctx, amount, context) =>
            {
                await ctx;
                var newCount = Interlocked.Add(ref count, amount);
                context.RejectCache();
                return $"{newCount}";
            }, TimeSpan.FromHours(1));

            TaskCompletionSource<string> waiting = new TaskCompletionSource<string>();
            TaskCompletionSource<string> done = new TaskCompletionSource<string>();
            done.SetResult("Result");

            count.Should().Be(0);

            Task<string> first = executor.ExecuteAsync("A", waiting.Task, 5);
            Task<string> second = executor.ExecuteAsync("A", done.Task, 5);

            first.IsCompleted.Should().BeFalse();
            second.IsCompleted.Should().BeFalse();
            waiting.SetResult("Result");

            var firstValue = (await first.Awaiting(t => t).Should().CompleteWithinAsync(1.Seconds())).Subject;
            var secondValue = (await second.Awaiting(t => t).Should().CompleteWithinAsync(1.Seconds())).Subject;

            firstValue.Should().NotBe(secondValue);
        }
    }
}
