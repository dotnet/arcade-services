using System;
using System.Threading;
using System.Threading.Tasks;

namespace Maestro.ScenarioTests
{
    public class AsyncDisposable : IAsyncDisposable
    {
        private Func<ValueTask> _dispose;

        public static IAsyncDisposable Create(Func<ValueTask> dispose)
        {
            return new AsyncDisposable(dispose);
        }

        private AsyncDisposable(Func<ValueTask> dispose)
        {
            _dispose = dispose;
        }

        public ValueTask DisposeAsync()
        {
            Func<ValueTask> dispose = Interlocked.Exchange(ref _dispose, null);
            return dispose?.Invoke() ?? new ValueTask(Task.CompletedTask);
        }
    }
}
