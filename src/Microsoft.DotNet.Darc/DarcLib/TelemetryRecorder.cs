// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;

namespace Microsoft.DotNet.DarcLib;

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
