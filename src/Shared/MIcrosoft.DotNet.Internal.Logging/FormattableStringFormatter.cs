using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Internal.Logging
{
    internal static class FormattableStringFormatter
    {
        private class FormattingLogger : ILogger, IDisposable
        {
            public string LastLog;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                LastLog = formatter(state, exception);
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public IDisposable BeginScope<TState>(TState state)
            {
                return this;
            }

            public void Dispose()
            {
            }
        }

        public static string Format(string logFormatString, object[] args)
        {
            var logger = new FormattingLogger();
            logger.Log(LogLevel.Error, logFormatString, args);
            return logger.LastLog;
        }
    }
}
