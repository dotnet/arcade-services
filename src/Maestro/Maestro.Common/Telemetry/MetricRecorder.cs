// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.Metrics;

namespace Maestro.Common.Telemetry;

public interface IMetricRecorder
{
    void QueueMessageReceived(TimeSpan timeInQueue);
}

public class MetricRecorder : IMetricRecorder
{
    private const string WaitTimeMetricName = "pcs.queue.wait_time";
    public const string MetricNamespace = "CustomMetrics";

    private readonly Counter<int> _queueWaitTimeCounter;

    public MetricRecorder(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MetricNamespace);
        _queueWaitTimeCounter = meter.CreateCounter<int>(WaitTimeMetricName);
    }

    public void QueueMessageReceived(TimeSpan timeInQueue)
    {
        _queueWaitTimeCounter.Add((int)timeInQueue.TotalSeconds);
    }
}
