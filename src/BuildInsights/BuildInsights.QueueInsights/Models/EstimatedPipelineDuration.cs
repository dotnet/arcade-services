// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.QueueInsights.Models;

public class EstimatedPipelineDuration
{
    public EstimatedPipelineDuration(int pipelineId, string pipelineName, TimeSpan mean,
        TimeSpan confidenceInterval)
    {
        PipelineId = pipelineId;
        PipelineName = pipelineName;
        Mean = mean;
        ConfidenceInterval = confidenceInterval;
    }

    public int PipelineId { get; }

    public string PipelineName { get; }

    public TimeSpan Mean { get; }

    public TimeSpan ConfidenceInterval { get; }

    public TimeSpan LowerBound => Mean - ConfidenceInterval;

    public TimeSpan UpperBound => Mean + ConfidenceInterval;
}
