// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Options;

namespace Maestro.Common.Telemetry;

public interface IMetricRecorder
{
    void QueueMessageReceived(int queueWaitTimeInSeconds);
    void TelemetryEventRecorded(
        string eventName,
        IReadOnlyDictionary<string, string>? dimensions = null,
        double? durationInMilliseconds = null);
}

public class MetricRecorder : IMetricRecorder
{
    private const string WaitTimeMetricName = "queue.wait_time";
    private const string TelemetryEventCountMetricName = "telemetry.event.count";
    private const string TelemetryEventDurationMetricName = "telemetry.event.duration";
    private const string EventNameDimension = "EventName";

    private readonly Counter<int> _queueWaitTimeCounter;
    private readonly Counter<int> _telemetryEventCounter;
    private readonly Histogram<double> _telemetryEventDuration;

    public MetricRecorder(IMeterFactory meterFactory, IOptions<TelemetryOptions> options)
    {
        var meter = meterFactory.Create(options.Value.MetricNamespace);
        _queueWaitTimeCounter = meter.CreateCounter<int>(WaitTimeMetricName);
        _telemetryEventCounter = meter.CreateCounter<int>(TelemetryEventCountMetricName);
        _telemetryEventDuration = meter.CreateHistogram<double>(TelemetryEventDurationMetricName, "ms");
    }

    public void QueueMessageReceived(int queueWaitTimeInSeconds)
    {
        _queueWaitTimeCounter.Add(queueWaitTimeInSeconds);
    }

    public void TelemetryEventRecorded(
        string eventName,
        IReadOnlyDictionary<string, string>? dimensions = null,
        double? durationInMilliseconds = null)
    {
        TagList tags = new()
        {
            { EventNameDimension, eventName }
        };

        if (dimensions is not null)
        {
            foreach ((string name, string value) in dimensions)
            {
                tags.Add(name, value);
            }
        }

        _telemetryEventCounter.Add(1, tags);
        if (durationInMilliseconds.HasValue)
        {
            _telemetryEventDuration.Record(durationInMilliseconds.Value, tags);
        }
    }
}
