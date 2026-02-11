// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.Json.Serialization;
using BuildInsights.KnownIssues.Models;

namespace BuildInsights.BuildAnalysis.Models;

public class TestResult : IResult
{
    public bool IsRetry { get; set; }

    public List<FailingConfiguration> FailingConfigurations { get; set; }

    public HelixWorkItem HelixWorkItem { get; set; }
    public bool IsKnownIssueFailure { get; set; }

    [JsonIgnore]
    public bool IsNewTest
    {
        get
        {
            return FailureRate.TotalRuns == 0;
        }
    }

    /// <summary>
    /// Creation date of the test
    /// </summary>
    public DateTimeOffset CreationDate { get; set; }
    public FailureRate FailureRate { get; set; }
    public IImmutableList<KnownIssue> KnownIssues { get; set; } = ImmutableList<KnownIssue>.Empty;

    /// <summary>
    /// Azure DevOps test case result object related to this Test Result.
    /// </summary>
    public TestCaseResult TestCaseResult { get; }

    public string AzDoUrl { get; }
    public string Url { get; }

    public TestResult() { }

    [JsonConstructor]
    public TestResult(TestCaseResult testCaseResult, string azDoUrl, FailureRate failureRate = null)
    {
        AzDoUrl = azDoUrl;
        TestCaseResult = testCaseResult;
        FailureRate = failureRate;
        Url = $"{AzDoUrl}/{testCaseResult.ProjectName}/_build/results?buildId={testCaseResult.BuildId}&view=ms.vss-test-web.build-test-results-tab&runId={testCaseResult.TestRunId}&resultId={testCaseResult.Id}";
    }
}
