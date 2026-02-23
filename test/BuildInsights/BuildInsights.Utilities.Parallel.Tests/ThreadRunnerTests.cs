using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;

namespace BuildInsights.Utilities.Parallel.Tests
{
    [TestFixture]
    public class ThreadRunnerTests
    {
        private struct TestData : IDisposable
        {
            public TestData(Action<IServiceCollection> configure)
            {
                var services = new ServiceCollection();
                services.AddSingleton<ThreadRunner>();
                services.AddScoped<ProcessingThreadIdentity>();
                services.AddLogging();
                configure(services);

                Provider = services.BuildServiceProvider();
                Runner = Provider.GetRequiredService<ThreadRunner>();
            }

            public ServiceProvider Provider { get; }
            public ThreadRunner Runner { get; }

            public void Dispose()
            {
                Provider?.Dispose();
            }
        }

        private class FailingProcessingThread : IProcessingThread
        {
            public int FailCount => _failCount;
            private int _failCount;
            public async Task RunAsync(CancellationToken cancellationToken)
            {
                await Task.Yield();
                Interlocked.Increment(ref _failCount);
                throw new ExpectedException();
            }
        }

        private class ExpectedException : Exception
        {
        }

        private TestData GetTestData(Action<IServiceCollection> configure)
        {
            return new TestData(configure);
        }

        [Test]
        public async Task FailingWorkShouldNotCrashLoop()
        {
            var thread = new FailingProcessingThread();
            using TestData data = GetTestData(services =>
            {
                services.AddSingleton<IProcessingThread>(thread);
                services.Configure<ParallelismSettings>(settings =>
                {
                    settings.CrashLoopDelaySeconds = 2;
                    settings.WorkerCount = 5;
                });
            });
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

            await data.Runner.Invoking(r => r.RunAsync(cts.Token))
                .Should().ThrowAsync<Exception>();
            thread.FailCount.Should().BeLessThan(7);
        }
    }
}
