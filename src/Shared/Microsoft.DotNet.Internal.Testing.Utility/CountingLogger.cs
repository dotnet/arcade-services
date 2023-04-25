// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Internal.Testing.Utility;

public class CountingLogger : ILoggerProvider, ILoggerFactory, ILogger
{
    public int Verbose = 0;
    public int Information = 0;
    public int Warning = 0;
    public int Error = 0;
    public int Exception = 0;
    public string CurrentOperation = null;
    public int OperationChanges = 0;

    private sealed class SetCurrent : IDisposable
    {
        private readonly CountingLogger _logger;
        private string _opName;

        public SetCurrent(CountingLogger logger)
        {
            _opName = logger.CurrentOperation;
            _logger = logger;
        }

        public void Dispose()
        {
            _logger.CurrentOperation = _opName;
        }
    }

    public void Dispose()
    {
    }

    ILogger ILoggerProvider.CreateLogger(string categoryName)
    {
        return this;
    }

    public void AddProvider(ILoggerProvider provider)
    {
        if (provider != this)
            throw new ArgumentException();
    }

    ILogger ILoggerFactory.CreateLogger(string categoryName)
    {
        return this;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
    {
        if (exception != null)
        {
            Exception++;
            return;
        }

        switch (logLevel)
        {
            case LogLevel.Trace:
            case LogLevel.Debug:
            case LogLevel.None:
                break;
            case LogLevel.Information:
                Information++;
                break;
            case LogLevel.Warning:
                Warning++;
                break;
            case LogLevel.Error:
            case LogLevel.Critical:
                Error++;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(logLevel), logLevel, null);
        }
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public IDisposable BeginScope<TState>(TState state)
    {
        var startOperation = new SetCurrent(this);
        CurrentOperation = state.ToString();
        OperationChanges++;
        return startOperation;
    }
}

public class CountingLogger<T> : ILogger<T>
{
    private readonly CountingLogger _generic;

    public CountingLogger(
        CountingLogger generic)
    {
        _generic = generic;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception exception,
        Func<TState, Exception, string> formatter)
    {
        _generic.Log(logLevel, eventId, state, exception, formatter);
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return _generic.IsEnabled(logLevel);
    }

    public IDisposable BeginScope<TState>(TState state)
    {
        return _generic.BeginScope(state);
    }
}
