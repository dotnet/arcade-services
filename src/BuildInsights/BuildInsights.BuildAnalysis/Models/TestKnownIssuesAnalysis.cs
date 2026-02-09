// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Microsoft.Internal.Helix.KnownIssues.Models;

namespace BuildInsights.BuildAnalysis.Models;

public class TestKnownIssuesAnalysis
{
    public TestKnownIssuesAnalysis()
    {
        TestResultWithKnownIssues = new List<TestResult>();
    }

    public TestKnownIssuesAnalysis(bool isAnalysisAvailable, List<TestResult> knownIssueTests)
    {
        IsAnalysisAvailable = isAnalysisAvailable;
        TestResultWithKnownIssues = knownIssueTests;
    }

    public bool IsAnalysisAvailable { get; set; }
    public List<TestResult> TestResultWithKnownIssues { get; set; }
}
