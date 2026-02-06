using System.Collections.Generic;
using Microsoft.Internal.Helix.KnownIssues.Models;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Models;

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
