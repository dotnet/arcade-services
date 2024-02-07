// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.ApplicationInsights;
using ProductConstructionService.Api.Queue.Jobs;
using ProductConstructionService.ServiceDefaults;

namespace ProductConstructionService.Api.Metrics;

public class TelemetryRecorder(ILogger<TelemetryRecorder> logger, TelemetryClient telemetryClient) : ITelemetryRecorder
{
    private readonly ILogger<TelemetryRecorder> _logger = logger;
    private readonly TelemetryClient _telemetryClient = telemetryClient;

    public ITelemetryScope RecordJob(Job job)
    {
        return new TelemetryScope($"{job.Type} completed", _logger, _telemetryClient);
    }

    private class TelemetryScope(string telemetryName, ILogger logger, TelemetryClient telemetryClient) : ITelemetryScope
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
            telemetryClient.TrackEvent(_telemetryName, new Dictionary<string, string>()
            {
                { "Duration", _stopwatch.ElapsedMilliseconds.ToString() },
                { "Status", GetSuccessfulString() }
            });
            logger.LogInformation("{telemetryName} took {duration} to complete {status}",
                _telemetryName,
                TimeSpan.FromMilliseconds(_stopwatch.ElapsedMilliseconds),
                _successful ? "successfully" : "unsuccessfully");
        }
    }
}
