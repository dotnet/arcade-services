using System;
using Microsoft.Build.Framework;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Maestro.Tasks
{
    public class MSBuildLogger : Microsoft.Extensions.Logging.ILogger
    {
        private readonly Build.Utilities.TaskLoggingHelper _log;

        public MSBuildLogger(Build.Utilities.TaskLoggingHelper log)
        {
            _log = log;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            var message = formatter(state, exception);
            switch (logLevel)
            {
                case LogLevel.Critical:
                    _log.LogCriticalMessage(null, null, null, null, 0, 0, 0, 0, message);
                    break;
                case LogLevel.Error:
                    _log.LogError(message);
                    break;
                case LogLevel.Warning:
                    _log.LogWarning(message);
                    break;
                case LogLevel.Information:
                    _log.LogMessage(MessageImportance.High, message);
                    break;
                case LogLevel.Debug:
                    _log.LogMessage(MessageImportance.Normal, message);
                    break;
                case LogLevel.Trace:
                    _log.LogMessage(MessageImportance.Low, message);
                    break;
            }
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return NullScope.Instance;
        }
    }

    /// <summary>
    /// An empty scope without any logic
    /// </summary>
    internal class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new NullScope();

        private NullScope()
        {
        }

        /// <inheritdoc />
        public void Dispose()
        {
        }
    }
}
