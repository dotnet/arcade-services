// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.BuildAnalysis.Models.Views;

namespace BuildInsights.BuildAnalysis.Models;

public class TestResultsGroupView
{
    public LinkToTestResultsView LinkToResultView { get; set; }
    public List<TestResultView> TestResults { get; set; }
    public int TotalUniqueTestFailures;
    public string PipelineName { get; set; }

    public bool HasTestResults => TestResults.Count > 0;
    public int DisplayTestsCount { get; set; } = 0;
    public int NotDisplayTestsCount => TotalUniqueTestFailures - DisplayTestsCount;
    public bool HasNotDisplayTests => NotDisplayTestsCount > 0;

    public TestResultsGroupView(string linkToTestResults, string pipelineName, List<TestResultView> testResults, int totalUniqueTestFailures)
    {
        LinkToResultView = new LinkToTestResultsView(linkToTestResults, $"<b>[All failing tests from {pipelineName}]</b>");
        PipelineName = pipelineName;
        TestResults = testResults;
        TotalUniqueTestFailures = totalUniqueTestFailures;
    }
}
