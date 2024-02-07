// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using Microsoft.ApplicationInsights;
using ProductConstructionService.Api.Queue.Jobs;

namespace ProductConstructionService.Api.Metrics;

public class TelemetryRecorder(ILogger<TelemetryRecorder> logger, TelemetryClient telemetryClient) : ITelemetryRecorder
{
    private readonly ILogger<TelemetryRecorder> _logger = logger;
    private readonly TelemetryClient _telemetryClient = telemetryClient;

    public ITelemetryScope RecordJob(Job job)
    {
        return new TelemetryScope($"{job.Type} completed", _logger, _telemetryClient, []);
    }

    private class TelemetryScope(string telemetryName, ILogger logger, TelemetryClient telemetryClient, Dictionary<string, string> customDimensions) : ITelemetryScope
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private bool _successful = false;

        public void SetSuccess()
        {
            _successful = true;
        }

        public void Dispose()
        {
            _stopwatch.Stop();
            customDimensions.Add("Successful", _successful.ToString());
            customDimensions.Add("Duration", _stopwatch.ToString());
            telemetryClient.TrackEvent(telemetryName, customDimensions);
            logger.LogInformation("{telemetryName} took {duration} to complete {status}",
                telemetryName,
                TimeSpan.FromMilliseconds(_stopwatch.ElapsedMilliseconds),
                _successful ? "successfully" : "unsuccessfully");
        }
    }
}
