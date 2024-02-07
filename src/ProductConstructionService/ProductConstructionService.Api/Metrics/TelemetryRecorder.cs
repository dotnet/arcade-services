// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.ApplicationInsights;
using ProductConstructionService.Api.Queue.Jobs;
using ProductConstructionService.ServiceDefaults;

namespace ProductConstructionService.Api.Metrics;

public class TelemetryRecorder(IMeterFactory meterFactory, ILogger<TelemetryRecorder> logger, TelemetryClient telemetryClient) : ITelemetryRecorder
{
    private readonly ILogger<TelemetryRecorder> _logger = logger;
    private readonly Meter _meter = meterFactory.Create(MetricConsts.JobMeterName);
    private readonly Dictionary<string, Histogram<long>> _histograms = [];
    private readonly TelemetryClient _telemetryClient = telemetryClient;

    public ITelemetryScope RecordJob(Job job)
    {
        var instrumentName = $"job.{job.Type}";

        if (!_histograms.TryGetValue(instrumentName, out var histogram))
        {
            var histogramName = $"job.{job.Type}.duration";

            histogram = _meter.CreateHistogram<long>(histogramName);
            _histograms.Add(instrumentName, histogram);
        }
        return new TelemetryScope(job.Type, histogram, _logger, _telemetryClient);
    }

    private class TelemetryScope(string telemetryName, Histogram<long> histogram, ILogger logger, TelemetryClient telemetryClient) : ITelemetryScope
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private bool _successful = false;
        private readonly string _telemetryName = telemetryName;
        private string GetSuccessfulString() => _successful ? "success" : "failure";

        public void SetSuccess()
        {
            _successful = true;
        }

        public void Dispose()
        {
            _stopwatch.Stop();
            histogram.Record(_stopwatch.ElapsedMilliseconds);
            telemetryClient.TrackEvent($"job.{_telemetryName}.{GetSuccessfulString()}");
            logger.LogInformation("{telemetryName} took {duration} to complete {status}",
                _telemetryName,
                TimeSpan.FromMilliseconds(_stopwatch.ElapsedMilliseconds),
                _successful ? "successfully" : "unsuccessfully");
        }
    }
}
