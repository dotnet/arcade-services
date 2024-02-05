// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.Metrics;
using ProductConstructionService.ServiceDefaults;

namespace ProductConstructionService.Api.Metrics;

public class MetricRecorder(IMeterFactory meterFactory)
{
    private readonly Meter _meter = meterFactory.Create(MetricConsts.JobMeterName);

    public void RecordJobDuration(string jobName, long duration)
    {
        var histogram = _meter.CreateHistogram<long>(GetJobDurationHistogramName(jobName));

        histogram.Record(duration);
    }

    private static string GetJobDurationHistogramName(string jobName) => $"job.{jobName}.duration";
}
