// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.Metrics;
using ProductConstructionService.Api.Queue.Jobs;
using ProductConstructionService.ServiceDefaults;

namespace ProductConstructionService.Api.Metrics;

public class MetricRecorder(IMeterFactory meterFactory, ILogger<MetricRecorder> logger) : IMetricRecorder
{
    private readonly ILogger<MetricRecorder> _logger = logger;
    private readonly Meter _meter = meterFactory.Create(MetricConsts.JobMeterName);
    private readonly Dictionary<string, Histogram<long>> jobDurationHistograms = new();

    public void RecordJobDuration(Job job, long duration)
    {
        var histogramName = $"job.{job.GetType().Name}.duration";
        if (!jobDurationHistograms.TryGetValue(histogramName, out var histogram))
        {
            histogram = _meter.CreateHistogram<long>(histogramName);
            jobDurationHistograms.Add(histogramName, histogram);
        }

        histogram.Record(duration);
        _logger.LogInformation("{jobType} {jobId} took {duration} to complete", job.GetType().Name, job.Id, TimeSpan.FromMilliseconds(duration));
    }
}
