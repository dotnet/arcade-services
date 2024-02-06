// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using ProductConstructionService.Api.Queue.Jobs;
using ProductConstructionService.ServiceDefaults;

namespace ProductConstructionService.Api.Metrics;

public class MetricRecorder(IMeterFactory meterFactory, ILogger<MetricRecorder> logger) : IMetricRecorder
{
    private readonly ILogger<MetricRecorder> _logger = logger;
    private readonly Meter _meter = meterFactory.Create(MetricConsts.JobMeterName);
    private readonly Dictionary<string, JobMetricRecorderInstruments> _jobMetricRecorderInstruments = new();

    public IMetricRecorderScope RecordJob(Job job)
    {
        var instrumentName = $"job.{job.Type}";

        if (!_jobMetricRecorderInstruments.TryGetValue(instrumentName, out var instruments))
        {
            var histogramName = $"job.{job.Type}.duration";
            var successCounterName = $"job.{job.Type}.success.count";
            var failureCounterName = $"job.{job.Type}.failure.count";

            instruments = new(
                _meter.CreateHistogram<long>(histogramName),
                _meter.CreateCounter<int>(successCounterName),
                _meter.CreateCounter<int>(failureCounterName));
            _jobMetricRecorderInstruments.Add(instrumentName, instruments);
        }

        return new MetricRecorderScope(job, instruments, _logger);
    }

    private class MetricRecorderScope(Job job, JobMetricRecorderInstruments instruments, ILogger logger) : IMetricRecorderScope
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private bool _jobSuccessful = false;

        public void SetSuccess()
        {
            _jobSuccessful = true;
        }

        public void Dispose()
        {
            _stopwatch.Stop();
            instruments.Histogram.Record(_stopwatch.ElapsedMilliseconds);
            logger.LogInformation("{jobType} {jobId} took {duration} to complete", job.Type, job.Id, TimeSpan.FromMilliseconds(_stopwatch.ElapsedMilliseconds));
            if (_jobSuccessful)
            {
                instruments.SuccessCounter.Add(1);
            }
            else
            {
                instruments.FailureCounter.Add(1);
            }
        }
    }
}
