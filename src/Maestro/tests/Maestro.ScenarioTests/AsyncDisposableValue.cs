using System;
using System.Threading.Tasks;

namespace Maestro.ScenarioTests
{
    public class AsyncDisposableValue<T> : IAsyncDisposable
    {
        private readonly Func<ValueTask> _dispose;

        public T Value { get; }

        internal AsyncDisposableValue(T value, Func<ValueTask> dispose)
        {
            Value = value;
            _dispose = dispose;
        }

        public ValueTask DisposeAsync()
        {
            return _dispose();
        }
    }

    public static class AsyncDisposableValue
    {
        public static AsyncDisposableValue<T> Create<T>(T value, Func<ValueTask> dispose)
        {
            return new AsyncDisposableValue<T>(value, dispose);
        }
    }
}
