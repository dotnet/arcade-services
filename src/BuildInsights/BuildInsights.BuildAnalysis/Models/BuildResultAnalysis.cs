// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Serialization;

namespace BuildInsights.BuildAnalysis.Models;

public enum BuildStatus
{
    Failed = 0,
    InProgress = 1,
    Succeeded = 2
}

public class BuildResultAnalysis
{
    public string PipelineName { get; set; }
    public int BuildId { get; set; }
    public string BuildNumber { get; set; } //In the form 20210226.40
    public Branch TargetBranch { get; set; }
    public string LinkToBuild { get; set; }
    public string LinkAllTestResults { get; set; }
    public int Attempt { get; set; }
    public bool IsRerun { get; set; }
    public BuildAutomaticRetry BuildAutomaticRetry { get; set; }
    public bool HasBuildFailures => BuildStepsResult != null && BuildStepsResult.Count > 0;
    public bool HasTestFailures => TestResults != null && TestResults.Count > 0;
    public BuildStatus BuildStatus { get; set; }
    public List<TestResult> TestResults { get; set; }
    public List<StepResult> BuildStepsResult { get; set; }
    public Attempt LatestAttempt { get; set; } //Latest attempt of the build
    public int TotalTestFailures { get; set; }
    public TestKnownIssuesAnalysis TestKnownIssuesAnalysis { get; set; }
}

public class Attempt
{
    public string LinkBuild { get; set; }
    public int AttemptId { get; set; }
    public List<StepResult> BuildStepsResult { get; set; }
    public List<TestResult> TestResults { get; set; }
    [JsonIgnore]
    public bool HasTestFailures => TestResults != null && TestResults.Count > 0;
    [JsonIgnore]
    public bool HasBuildFailures => BuildStepsResult != null && BuildStepsResult.Count > 0;
}
