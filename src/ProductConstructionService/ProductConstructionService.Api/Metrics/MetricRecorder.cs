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
    private readonly Dictionary<string, JobMetricRecorderInstruments> _jobMetricRecorderInstruments = [];

    public IMetricScope RecordJob(Job job)
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

        return new MetricScope($"{job.Type} {job.Id}", instruments, _logger);
    }

    private class MetricScope(string metricName, JobMetricRecorderInstruments instruments, ILogger logger) : IMetricScope
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private bool _successful = false;
        private readonly string _metricName = metricName;

        public void SetSuccess()
        {
            _successful = true;
        }

        public void Dispose()
        {
            _stopwatch.Stop();
            instruments.Record(_stopwatch.ElapsedMilliseconds, _successful);
            logger.LogInformation("{metricName} took {duration} to complete {status}",
                _metricName,
                TimeSpan.FromMilliseconds(_stopwatch.ElapsedMilliseconds),
                _successful ? "successfully" : "unsuccessfully");
        }
    }
}
