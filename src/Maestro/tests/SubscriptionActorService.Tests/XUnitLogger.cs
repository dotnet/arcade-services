using System;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace SubscriptionActorService.Tests
{
    public class XUnitLogger : ILogger, ILoggerProvider
    {
        private readonly ITestOutputHelper _output;

        public XUnitLogger(ITestOutputHelper output)
        {
            _output = output;
        }

        public void Dispose()
        {
        }

        public ILogger CreateLogger(string categoryName)
        {
            return this;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            _output.WriteLine($"{logLevel} : {eventId} : {formatter(state, exception)}");
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
