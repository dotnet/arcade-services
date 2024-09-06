// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.Metrics;
using Azure.Storage.Queues.Models;

namespace ProductConstructionService.Common;

public interface IMetricRecorder
{
    void QueueMessageReceived(QueueMessage message, TimeSpan delay);
}

public class MetricRecorder : IMetricRecorder
{
    public const string PcsMetricsNamespace = "ProductConstructionService.Metrics";
    private const string WaitTimeMetricName = "pcs.queue.wait_time";

    private readonly Counter<int> _queueWaitTimeCounter;

    public MetricRecorder(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(PcsMetricsNamespace);
        _queueWaitTimeCounter = meter.CreateCounter<int>(WaitTimeMetricName);
    }

    public void QueueMessageReceived(QueueMessage message, TimeSpan delay)
    {
        TimeSpan timeInQueue = DateTimeOffset.UtcNow - message.InsertedOn!.Value - delay;
        _queueWaitTimeCounter.Add((int)timeInQueue.TotalSeconds);
    }
}
