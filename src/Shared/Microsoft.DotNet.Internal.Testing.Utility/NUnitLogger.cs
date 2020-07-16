using System;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Microsoft.DotNet.Internal.Testing.Utility
{
    public class NUnitLogger : ILogger, ILoggerProvider
    {
        public void Dispose()
        {
        }

        public ILogger CreateLogger(string categoryName)
        {
            return this;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            TestContext.WriteLine($"{logLevel} : {eventId} : {formatter(state, exception)}");
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return this;
        }
    }
}
