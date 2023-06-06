using System;
using System.Threading;

namespace Maestro.ScenarioTests
{
    public class Disposable : IDisposable
    {
        private Action _dispose;

        public static IDisposable Create(Action dispose)
        {
            return new Disposable(dispose);
        }

        private Disposable(Action dispose)
        {
            _dispose = dispose;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _dispose, null)?.Invoke();
        }
    }
}
