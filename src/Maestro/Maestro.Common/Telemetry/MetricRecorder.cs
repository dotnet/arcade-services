// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.Metrics;

namespace Maestro.Common.Telemetry;

public interface IMetricRecorder
{
    void QueueMessageReceived(int queueWaitTimeInSeconds);
}

public class MetricRecorder : IMetricRecorder
{
    public const string BIMetricsNamespace = "BuildInsights.Metrics";
    public const string PcsMetricsNamespace = "ProductConstructionService.Metrics";
    private const string WaitTimeMetricName = "pcs.queue.wait_time";

    private readonly Counter<int> _queueWaitTimeCounter;

    public MetricRecorder(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(PcsMetricsNamespace);
        _queueWaitTimeCounter = meter.CreateCounter<int>(WaitTimeMetricName);
    }

    public void QueueMessageReceived(int queueWaitTimeInSeconds)
    {
        _queueWaitTimeCounter.Add(queueWaitTimeInSeconds);
    }
}
