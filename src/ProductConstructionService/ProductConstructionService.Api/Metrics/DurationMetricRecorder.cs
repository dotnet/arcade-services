// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.Metrics;
using ProductConstructionService.ServiceDefaults;

namespace ProductConstructionService.Api.Metrics;

public class DurationMetricRecorder(IMeterFactory meterFactory)
{
    private readonly Meter _meter = meterFactory.Create(MetricConsts.JobMeterName);

    public void Record(string name, long value)
    {
        var histogram = _meter.CreateHistogram<long>(name);

        histogram.Record(value);
    }
}
