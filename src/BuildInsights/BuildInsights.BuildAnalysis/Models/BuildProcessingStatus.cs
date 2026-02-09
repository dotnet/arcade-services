// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.BuildAnalysis.Models;

public class BuildProcessingStatus
{
    public string Value { get; }

    public BuildProcessingStatus(string status)
    {
        Value = status;
    }

    public static BuildProcessingStatus InProcess => new BuildProcessingStatus("InProcess");
    public static BuildProcessingStatus Completed => new BuildProcessingStatus("Completed");
    public static BuildProcessingStatus ConclusionOverridenByUser => new BuildProcessingStatus("ConclusionOverridenByUser");
}
