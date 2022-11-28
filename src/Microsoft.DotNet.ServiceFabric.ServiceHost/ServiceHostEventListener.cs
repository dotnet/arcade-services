using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost;

public class ServiceHostEventListener : EventListener
{
    private static ServiceHostEventListener _instance;
    private static IImmutableList<string> _eventSourceNamePrefixes;
    private static ILoggerFactory _loggerFactory;
    private static ConcurrentDictionary<string, ILogger> _loggers = new();

    public static ServiceHostEventListener ListenToEventSources(ILoggerFactory loggerFactory, params string[] namePrefixes)
    {
        if (_instance is not null)
        {
            throw new InvalidOperationException("Cannot call ServiceHostEventListener.ListenToEventSources more than once.");
        }
        // These fields must be static because the "OnEventSourceCreated" method gets called inside the base constructor, so instance fields aren't initialized when it runs
        _eventSourceNamePrefixes = namePrefixes.ToImmutableList();
        _loggerFactory = loggerFactory;
        return _instance = new ServiceHostEventListener();
    }


    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (_eventSourceNamePrefixes.Any(prefix => eventSource.Name.StartsWith(prefix)))
        {
            EnableEvents(eventSource, EventLevel.Warning);
            _loggers.TryAdd(eventSource.Name, _loggerFactory.CreateLogger(eventSource.Name));
        }
        base.OnEventSourceCreated(eventSource);
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        if (!_loggers.TryGetValue(eventData.EventSource.Name, out var logger))
        {
            return;
        }

        var level = eventData.Level switch
        {
            EventLevel.LogAlways => LogLevel.Critical,
            EventLevel.Critical => LogLevel.Critical,
            EventLevel.Error => LogLevel.Error,
            EventLevel.Warning => LogLevel.Warning,
            EventLevel.Informational => LogLevel.Information,
            EventLevel.Verbose => LogLevel.Trace,
            _ => LogLevel.None,
        };


        if (eventData.PayloadNames is not null && eventData.Payload is not null)
        {
            logger.Log(level, 
                0,
                new EventListenerLogValues(eventData.EventName, eventData.Message, eventData.PayloadNames, eventData.Payload), 
                null,
                (v, _) => v.ToString());
        }
        else if (eventData.Payload is not null)
        {
            var sb = new StringBuilder(4 + eventData.EventName.Length + eventData.Message.Length + eventData.Payload.Count * 8);
            sb.Append(eventData.EventName);
            sb.Append(": ");
            sb.AppendFormat(eventData.Message, eventData.Payload.ToArray());
            sb.Append("; ");
            for (int i = 0; i < eventData.Payload.Count; i++)
            {
                if (i != 0)
                {
                    sb.Append(", ");
                }
                sb.Append(eventData.Payload[i]);
            }

            logger.Log(level, sb.ToString());
        }
        else
        {
            logger.Log(level, $"{eventData.EventName}: {eventData.Message}");
        }
    }
}

public readonly struct EventListenerLogValues : IReadOnlyList<KeyValuePair<string, object>>
{
    private readonly IReadOnlyList<string> _names;
    private readonly IReadOnlyList<object> _values;
    private readonly string _message;
    private readonly string _eventName;

    public EventListenerLogValues(string eventName, string message, ReadOnlyCollection<string> names, ReadOnlyCollection<object> values)
    {
        _eventName = eventName;
        _message = message;
        _names = names;
        _values = values;
    }

    public override string ToString()
    {
        var b = new StringBuilder(4 + _eventName.Length + _message.Length + _names.Count * 16);
        b.Append(_eventName);
        b.Append(": ");
        b.AppendFormat(_message, _values.ToArray());
        b.Append("; ");
        for (int i = 0; i < _names.Count; i++)
        {
            if (i != 0)
            {
                b.Append(", ");
            }
            b.Append(_names[i]);
            b.Append(" = ");
            b.Append(_values[i]);
        }

        return b.ToString();
    }

    public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
    {
        for (int i = 0; i < _names.Count; i++)
        {
            yield return KeyValuePair.Create(_names[i], _values[i]);
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public int Count => _names.Count;

    public KeyValuePair<string, object> this[int index] => KeyValuePair.Create(_names[index], _values[index]);
}
