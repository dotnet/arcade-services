// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Maestro.Common.Telemetry;

/// <summary>
/// A no-op implementation of ITelemetryRecorder, useful for testing or when telemetry is not needed.
/// </summary>
public class NoTelemetryRecorder : ITelemetryRecorder
{
    private static readonly NoTelemetryScope _instance = new();

    public ITelemetryScope RecordWorkItemCompletion(string workItemName, long attemptNumber, string operationId) => _instance;
    public ITelemetryScope RecordGitOperation(TrackedGitOperation operation, string repoUri) => _instance;
    public void RecordCustomEvent(CustomEventType eventName, Dictionary<string, string> customProperties) { }

    public class NoTelemetryScope : ITelemetryScope
    {
        public void Dispose() { }
        public void SetSuccess() { }
    }
}
