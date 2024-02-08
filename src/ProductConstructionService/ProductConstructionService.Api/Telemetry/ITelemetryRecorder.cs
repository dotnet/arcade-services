// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.Api.Queue.Jobs;

namespace ProductConstructionService.Api.Telemetry;

public interface ITelemetryRecorder
{
    /// <summary>
    /// Records job durationand result in the customEvents table, with the `job.{jobType}` name
    /// </summary>
    ITelemetryScope RecordJob(Job job);
}
