using System;
using System.Threading;

namespace Maestro.ScenarioTests
{
    public sealed class Shareable<T> : IDisposable where T : class, IDisposable
    {
        private T _inner;

        internal Shareable(T inner)
        {
            _inner = inner;
        }

        public void Dispose()
        {
            _inner?.Dispose();
        }

        public T TryTake()
        {
            return Interlocked.Exchange(ref _inner, null);
        }

        public T Peek()
        {
            if (_inner == null)
            {
                throw new InvalidOperationException("Peek() called on null Shareable.");
            }
            return _inner;
        }
    }

    public static class Shareable
    {
        public static Shareable<T> Create<T>(T target) where T : class, IDisposable
        {
            return new Shareable<T>(target);
        }
    }
}
