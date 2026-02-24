// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Logging;

namespace Maestro.Common.Telemetry;

public enum TrackedGitOperation
{
    Clone,
    Fetch,
    Push,
}

public enum CustomEventType
{
    PullRequestUpdateFailed
}

public interface ITelemetryRecorder
{
    /// <summary>
    /// Records work item duration and result in the customEvents table, with the `workItem.{type}` name
    /// </summary>
    ITelemetryScope RecordWorkItemCompletion(string workItemName, long attemptNumber, string operationId);

    /// <summary>
    /// Records git operation duration and result.
    /// </summary>
    ITelemetryScope RecordGitOperation(TrackedGitOperation operation, string repoUri);

    /// <summary>
    /// Records a custom event with the given name and custom properties.
    /// </summary>
    void RecordCustomEvent(CustomEventType customEvent, Dictionary<string, string> customProperties);
}

public interface ITelemetryScope : IDisposable
{
    /// <summary>
    /// Marks the operation running in the scope as successful, always call this method before disposing the scope
    /// </summary>
    void SetSuccess();
}

public class TelemetryRecorder(
        ILogger<TelemetryRecorder> logger,
        TelemetryClient telemetryClient)
    : ITelemetryRecorder
{
    private readonly ILogger<TelemetryRecorder> _logger = logger;
    private readonly TelemetryClient _telemetryClient = telemetryClient;

    public ITelemetryScope RecordWorkItemCompletion(string workItemType, long attemptNumber, string operationId)
        => CreateScope("WorkItemExecuted", new()
            {
                { "WorkItemType", workItemType },
                { "Attempt", attemptNumber.ToString()},
                { "OperationId", operationId }
            });

    public ITelemetryScope RecordGitOperation(TrackedGitOperation operation, string repoUri)
        => CreateScope($"Git{operation}", new() { { "Uri", repoUri } });

    public void RecordCustomEvent(CustomEventType eventName, Dictionary<string, string> customProperties)
    {
        _telemetryClient.TrackEvent(eventName.ToString(), customProperties);
    }

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
