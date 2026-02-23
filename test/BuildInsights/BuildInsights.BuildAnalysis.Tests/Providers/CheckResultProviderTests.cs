// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using AwesomeAssertions;
using BuildInsights.BuildAnalysis.Models;
using BuildInsights.GitHub.Models;
using BuildInsights.KnownIssues.Models;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using NUnit.Framework;

namespace BuildInsights.BuildAnalysis.Tests.Providers;

[TestFixture]
public class CheckResultProviderTests
{
    private readonly CheckResultProvider _checkResultProvider = new(new TelemetryClient(new TelemetryConfiguration()));

    [TestCase(true, CheckResult.Failed)]
    [TestCase(false, CheckResult.Failed)]
    public void BuildStatusWithStepFailures(bool isKnownIssueResult, CheckResult expectedResult)
    {
        ImmutableList<StepResult> stepResults = ImmutableList.Create(MockStepResult([]));
        BuildResultAnalysis buildAnalysis = MockBuildResultAnalysis(BuildStatus.Failed, stepResults.ToList(), []);

        CheckResult result = _checkResultProvider.GetCheckResult(MockBuildReference(), ImmutableList.Create(buildAnalysis), 0, isKnownIssueResult);
        result.Should().Be(expectedResult);
    }

    [TestCase(true, CheckResult.Passed)]
    [TestCase(false, CheckResult.Failed)]
    public void BuildStatusWithStepKnownIssuesFailuresKnown(bool isKnownIssueResult, CheckResult expectedResult)
    {
        var knownIssues = new List<KnownIssue>
            {new(new GitHubIssue(), ["Any build error"], KnownIssueType.Repo, new KnownIssueOptions())};
        var stepResults = new List<StepResult> { MockStepResult(knownIssues) };
        BuildResultAnalysis buildAnalysis = MockBuildResultAnalysis(BuildStatus.Failed, stepResults, []);

        CheckResult result =
            _checkResultProvider.GetCheckResult(MockBuildReference(), ImmutableList.Create(buildAnalysis), 0, isKnownIssueResult);
        result.Should().Be(expectedResult);
    }

    [TestCase(true, CheckResult.Passed)]
    [TestCase(false, CheckResult.Failed)]
    public void BuildStatusWithTestKnownIssuesFailuresKnown(bool isKnownIssueResult, CheckResult expectedResult)
    {
        TestResult testResult = MockTestResult();
        testResult.KnownIssues = ImmutableList.Create(new KnownIssue(new GitHubIssue(),
            ["Any build error"], KnownIssueType.Repo, new KnownIssueOptions()));
        var testResults = new List<TestResult> { testResult };

        BuildResultAnalysis buildAnalysis = MockBuildResultAnalysis(BuildStatus.Failed, [], testResults);

        CheckResult result = _checkResultProvider.GetCheckResult(MockBuildReference(), ImmutableList.Create(buildAnalysis),
            0, isKnownIssueResult);
        result.Should().Be(expectedResult);
    }

    [TestCase(true, CheckResult.Failed)]
    [TestCase(false, CheckResult.Failed)]
    public void BuildStatusWithTestKnownIssuesFailuresUnique(bool isKnownIssueResult, CheckResult expectedResult)
    {
        TestResult testResult = MockTestResult();
        var testResults = new List<TestResult> { testResult };

        BuildResultAnalysis buildAnalysis = MockBuildResultAnalysis(BuildStatus.Failed, [], testResults);

        CheckResult result = _checkResultProvider.GetCheckResult(MockBuildReference(), ImmutableList.Create(buildAnalysis),
            0, isKnownIssueResult);
        result.Should().Be(expectedResult);
    }

    [TestCase(BuildStatus.Failed, BuildStatus.Succeeded, 0, CheckResult.Failed)]
    [TestCase(BuildStatus.Succeeded, BuildStatus.Succeeded, 0, CheckResult.Passed)]
    [TestCase(BuildStatus.Succeeded, BuildStatus.InProgress, 1, CheckResult.InProgress)]
    public void GetOverallCheckResult(BuildStatus buildStatusA, BuildStatus buildStatusB, int pending,
        CheckResult expectedResult)
    {
        var buildAnalysis = new List<BuildResultAnalysis>
        {
            MockBuildResultAnalysis(buildStatusA, [], []),
            MockBuildResultAnalysis(buildStatusB, [], [])
        };

        CheckResult result = _checkResultProvider.GetCheckResult(MockBuildReference(), buildAnalysis.ToImmutableList(), pending, false);

        result.Should().Be(expectedResult);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void GetOverallBuildStatusNoResults(bool isKnownIssues)
    {
        CheckResult result = _checkResultProvider.GetCheckResult(MockBuildReference(), ImmutableList<BuildResultAnalysis>.Empty, 0, isKnownIssues);
        result.Should().Be(CheckResult.InProgress);
    }

    private NamedBuildReference MockBuildReference()
    {
        return new NamedBuildReference("", "", "", "", 12345, "", 6789, "", "", "", "");
    }

    private TestResult MockTestResult()
    {
        return new TestResult(
            new TestCaseResult("A", new DateTimeOffset(2021, 5, 28, 11, 0, 0, TimeSpan.Zero),
                TestOutcomeValue.Failed, 0, 0, 0, new PreviousBuildRef(), "", "", "", null, 55000), "",
            new FailureRate());
    }

    private StepResult MockStepResult(List<KnownIssue> knownIssues = null)
    {
        return new StepResult
        {
            StepName = "StepNameError",
            Errors =
            [
                new() {ErrorMessage = "StepErrorMessage"}
            ],
            FailureRate = new FailureRate { TotalRuns = 0 },
            KnownIssues = knownIssues?.ToImmutableList() ?? ImmutableList<KnownIssue>.Empty
        };
    }

    public BuildResultAnalysis MockBuildResultAnalysis(BuildStatus buildStatus, List<StepResult> stepResults, List<TestResult> testResults)
    {
        return new BuildResultAnalysis
        {
            PipelineName = "PIPELINE_TEST",
            BuildId = 123456,
            BuildNumber = "2021.23.34",
            BuildStatus = buildStatus,
            TestResults = testResults,
            BuildStepsResult = stepResults,
            TotalTestFailures = 2,
            TestKnownIssuesAnalysis = new TestKnownIssuesAnalysis(true, [])
        };
    }
}
