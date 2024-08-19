// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.DotNet.DarcLib;

public enum TrackedGitOperation
{
    Clone,
    Fetch,
    Push,
}

public interface ITelemetryRecorder
{
    /// <summary>
    /// Records job duration and result in the customEvents table, with the `job.{jobType}` name
    /// </summary>
    ITelemetryScope RecordWorkItemCompletion(string jobName);

    /// <summary>
    /// Records git operation duration and result.
    /// </summary>
    ITelemetryScope RecordGitOperation(TrackedGitOperation operation, string repoUri);
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

    public ITelemetryScope RecordWorkItemCompletion(string jobName) => _instance;
    public ITelemetryScope RecordGitOperation(TrackedGitOperation operation, string repoUri) => _instance;

    public class NoTelemetryScope : ITelemetryScope
    {
        public void Dispose() { }
        public void SetSuccess() { }
    }
}
