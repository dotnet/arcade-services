using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using NUnit.Framework;

namespace Microsoft.Internal.Helix.Utility.Parallel.Tests
{
    [TestFixture]
    public class LimitedParallelTest
    {
        [Test]
        public async Task Zero()
        {
            var p = new ParallelCounter();
            var results = await LimitedParallel.WhenAll(new int[0], i => p.Track(Delay(i)), 5);
            results.Should().BeEmpty();
            p.MaxParallel.Should().Be(0);
        }

        [Test]
        public async Task ZeroAsync()
        {
            var p = new ParallelCounter();
            var results = await LimitedParallel.WhenAllAsync(new int[0], i => p.Track(Delay(i)), 5).ToListAsync();
            results.Should().BeEmpty();
            p.MaxParallel.Should().Be(0);
        }

        [Test]
        public async Task BadParallelismThrows()
        {
            var p = new ParallelCounter();
            Func<Task> whenAllNegativeOne = () => LimitedParallel.WhenAll(new int[0], i => p.Track(Delay(i)), -1);
            await whenAllNegativeOne.Should().ThrowExactlyAsync<ArgumentOutOfRangeException>();
            p.MaxParallel.Should().Be(0);
        }

        [Test]
        public void BadParallelismThrowsAsync()
        {
            var p = new ParallelCounter();
            Func<Task<List<int>>> whenAllNegativeOne = () => LimitedParallel.WhenAllAsync(new int[0], i => p.Track(Delay(i)), -1).ToListAsync().AsTask();
            whenAllNegativeOne.Should().ThrowExactlyAsync<ArgumentOutOfRangeException>();
            p.MaxParallel.Should().Be(0);
        }

        [Test]
        public async Task NullExecutorThrows()
        {
            Func<Task> whenNull = () => LimitedParallel.WhenAll(new int[0], (Func<int, Task<int>>)null, 5);
            await whenNull.Should().ThrowExactlyAsync<ArgumentNullException>();
        }

        [Test]
        public void NullExecutorThrowsAsync()
        {
            Func<Task<List<int>>> whenNull = () => LimitedParallel.WhenAllAsync(new int[0], (Func<int, Task<int>>)null, 5).ToListAsync().AsTask();
            whenNull.Should().ThrowExactlyAsync<ArgumentNullException>();
        }
        
        [Test]
        public async Task NullSourceThrows()
        {
            var p = new ParallelCounter();
            Func<Task> whenNull = () => LimitedParallel.WhenAll((int[])null, i => p.Track(Delay(i)), 5);
            await whenNull.Should().ThrowExactlyAsync<ArgumentNullException>();
            p.MaxParallel.Should().Be(0);
        }

        [Test]
        public void NullSourceThrowsAsync()
        {
            var p = new ParallelCounter();
            Func<IAsyncEnumerable<int>> whenNull = () => LimitedParallel.WhenAllAsync((int[])null, i => p.Track(Delay(i)), 5);
            whenNull.Should().ThrowExactly<ArgumentNullException>();
            p.MaxParallel.Should().Be(0);
        }

        [Test]
        public async Task UnderPopulatedRunsParallel()
        {
            var p = new ParallelCounter();
            Stopwatch w = new Stopwatch();
            w.Start();
            var results = await LimitedParallel.WhenAll(new int[] { 1, 2, 3 }, i => p.Track(Delay(i)), 5);
            w.Stop();
            results.Should().Equal(new[] { 101, 102, 103 });
            p.MaxParallel.Should().Be(3);
        }

        [Test]
        public async Task UnderPopulatedRunsParallelAsync()
        {
            var p = new ParallelCounter();
            Stopwatch w = new Stopwatch();
            w.Start();
            var results = await LimitedParallel.WhenAllAsync(new int[] { 1, 2, 3 }, i => p.Track(Delay(i)), 5).ToListAsync();
            w.Stop();
            results.Should().BeEquivalentTo(new[] { 101, 102, 103 });
            p.MaxParallel.Should().Be(3);
        }

        [Test]
        public async Task OverPopulatedRunsThrottled()
        {
            var p = new ParallelCounter();
            Stopwatch w = new Stopwatch();
            w.Start();
            var results = await LimitedParallel.WhenAll(
                new int[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 },
                i => p.Track(Delay(i)),
                5);
            w.Stop();
            results.Should().Equal(new[] { 101, 102, 103, 104, 105, 106, 107, 108, 109, 110 });
            p.MaxParallel.Should().Be(5);
        }

        [Test]
        public async Task OverPopulatedRunsThrottledAsync()
        {
            var p = new ParallelCounter();
            Stopwatch w = new Stopwatch();
            w.Start();
            List<int> results = await LimitedParallel.WhenAllAsync(
                new int[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 },
                i => p.Track(Delay(i)),
                5).ToListAsync().AsTask();
            w.Stop();
            results.Should().BeEquivalentTo(new[] { 101, 102, 103, 104, 105, 106, 107, 108, 109, 110 });
            p.MaxParallel.Should().Be(5);
        }

        private Task<int> Delay(int i)
        {
            return Task.Delay(100).ContinueWith(_ => i + 100);
        }

        private class ParallelCounter
        {
            public int MaxParallel = 0;
            public int CurrentParallel = 0;

            private readonly object _updateLock = new object();

            public async Task<T> Track<T>(Task<T> thing)
            {
                lock (_updateLock)
                {
                    CurrentParallel++;
                    MaxParallel = Math.Max(MaxParallel, CurrentParallel);
                }

                var t = await thing;

                lock (_updateLock)
                {
                    CurrentParallel--;
                }

                return t;
            }
        }
    }
}
