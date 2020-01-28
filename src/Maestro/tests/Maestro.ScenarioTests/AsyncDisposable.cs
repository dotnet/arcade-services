using System;
using System.Threading.Tasks;

namespace Maestro.ScenarioTests
{
    public class AsyncDisposable : IAsyncDisposable
    {
        private readonly Func<ValueTask> _dispose;

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
            return _dispose();
        }
    }
}
