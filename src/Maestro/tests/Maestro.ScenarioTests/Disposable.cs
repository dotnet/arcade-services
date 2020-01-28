using System;

namespace Maestro.ScenarioTests
{
    public class Disposable : IDisposable
    {
        private readonly Action _dispose;

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
            _dispose();
        }
    }
}
