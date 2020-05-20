using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost.Tests
{
    public class ExtensionsTests
    {
        [Fact]
        public async Task NonCancelableThrows()
        {
            await Assert.ThrowsAsync<ArgumentException>(() => CancellationToken.None.AsTask());
        }

        [Fact]
        public void PostCancellationChangesStateCompleted()
        {
            var source = new CancellationTokenSource();
            Task task = source.Token.AsTask();
            Assert.False(task.IsCompleted);
            source.Cancel();
            Assert.True(task.IsCompleted);
        }

        [Fact]
        public void PreCancelledReturnsCompleted()
        {
            var source = new CancellationTokenSource();
            source.Cancel();
            Task task = source.Token.AsTask();
            Assert.True(task.IsCompleted);
        }

        [Fact]
        public void WithoutCancellingNotComplete()
        {
            var source = new CancellationTokenSource();
            Task task = source.Token.AsTask();
            Assert.False(task.IsCompleted);
        }
    }
}
