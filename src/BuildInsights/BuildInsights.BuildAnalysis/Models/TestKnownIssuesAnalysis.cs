// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.BuildAnalysis.Models;

public class TestKnownIssuesAnalysis
{
    public TestKnownIssuesAnalysis()
    {
        TestResultWithKnownIssues = [];
    }

    public TestKnownIssuesAnalysis(bool isAnalysisAvailable, List<TestResult> knownIssueTests)
    {
        IsAnalysisAvailable = isAnalysisAvailable;
        TestResultWithKnownIssues = knownIssueTests;
    }

    public bool IsAnalysisAvailable { get; set; }
    public List<TestResult> TestResultWithKnownIssues { get; set; }
}
