﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using Microsoft.ApplicationInsights;
using Microsoft.DotNet.DarcLib;

namespace ProductConstructionService.Api.Telemetry;

public class TelemetryRecorder(
        ILogger<TelemetryRecorder> logger,
        TelemetryClient telemetryClient)
    : ITelemetryRecorder
{
    private readonly ILogger<TelemetryRecorder> _logger = logger;
    private readonly TelemetryClient _telemetryClient = telemetryClient;

    public ITelemetryScope RecordJobCompletion(string jobType)
        => CreateScope("JobExecuted", new() {{ "JobType", jobType }});

    public ITelemetryScope RecordGitOperation(TrackedGitOperation operation, string repoUri)
        => CreateScope($"Git{operation}", new() { { "Uri", repoUri } });

    private TelemetryScope CreateScope(string metricName, Dictionary<string, string> customDimensions)
    {
        return new TelemetryScope(metricName, _logger, _telemetryClient, customDimensions, []);
    }

    private class TelemetryScope(
        string telemetryName,
        ILogger logger,
        TelemetryClient telemetryClient,
        Dictionary<string, string> customDimensions,
        Dictionary<string, double> customMeasurement) : ITelemetryScope
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private bool _successful = false;

        private const string SuccessDimension = "Success";
        private const string DurationMeasurement = "Duration";

        private string GetSuccessString() => _successful ? "successfully" : "unsuccessfully";

        public void SetSuccess()
        {
            _successful = true;
        }

        public void Dispose()
        {
            _stopwatch.Stop();
            customDimensions.Add(SuccessDimension, _successful.ToString());
            customMeasurement.Add(DurationMeasurement, _stopwatch.ElapsedMilliseconds);
            telemetryClient.TrackEvent(telemetryName, customDimensions, customMeasurement);
            logger.LogInformation("{telemetryName} took {duration} to complete {status}",
                telemetryName,
                TimeSpan.FromMilliseconds(_stopwatch.ElapsedMilliseconds),
                GetSuccessString());
        }
    }
}
