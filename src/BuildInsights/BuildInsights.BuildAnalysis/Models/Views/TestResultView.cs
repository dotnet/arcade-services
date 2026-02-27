// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using BuildInsights.KnownIssues;
using BuildInsights.KnownIssues.Models;

#nullable disable
namespace BuildInsights.BuildAnalysis.Models.Views;

public class TestResultView : IResult
{
    public int BuildId { get; }
    public string TestName { get; set; }
    public bool IsRetry { get; set; }
    public bool IsFlakyTest { get; set; }
    public bool IsHelixWorkItem => HelixWorkItem != null;
    public bool IsHelixWorkItemFailure => IsHelixWorkItem && HelixWorkItem.ExitCode != 0 & string.IsNullOrEmpty(CallStack);
    public int Attempt { get; set; }
    public string TestLogs { get; set; }
    public string HistoryLink { get; set; }
    public string ArtifactLink { get; set; }
    public string ConsoleLogLink { get; set; }
    public HelixWorkItem HelixWorkItem { get; set; }
    public List<FailingConfiguration> FailingConfigurations { get; set; }
    public List<TestSubResultView> TestSubResults { get; set; }
    public bool HasTestSubResults => TestSubResults != null && TestSubResults.Count > 0;
    public string ReportKnownIssueInfraTestUrl { get; set; }
    public string ReportKnownIssueRepositoryTestUrl { get; set; }

    public bool IsNewTest => FailureRate?.TotalRuns == 0;
    public DateTimeOffset CreationDate { get; set; } //Creation date of the test
    public string ExceptionMessage { get; set; }
    public string CallStack { get; set; }
    private string BuildUrl { get; set; }
    public FailureRate FailureRate { get; set; }
    public IImmutableList<KnownIssue> KnownIssues { get; set; } = ImmutableList<KnownIssue>.Empty;

    private readonly string _azDOUrl;

    public TestResultView()
    {
    }

    public TestResultView(TestResult testResult, int buildId, string buildUrl, MarkdownParameters markdownParameters)
    {
        BuildId = buildId;
        _azDOUrl = testResult.AzDoUrl;
        BuildUrl = buildUrl;
        TestName = testResult.TestCaseResult.Name;
        IsRetry = testResult.IsRetry;
        IsFlakyTest = testResult.TestCaseResult.Outcome == TestOutcomeValue.PassedOnRerun;
        HelixWorkItem = testResult.HelixWorkItem;
        Attempt = testResult.TestCaseResult.Attempt;
        TestLogs = BuildTestResultsTabUri(testResult.TestCaseResult, "debug");
        HistoryLink = BuildTestResultsTabUri(testResult.TestCaseResult, "history");
        ArtifactLink = BuildTestLogsWebUri(testResult.TestCaseResult);
        FailingConfigurations = testResult.FailingConfigurations;
        CreationDate = testResult.CreationDate;
        ExceptionMessage = testResult.TestCaseResult.ErrorMessage;
        CallStack = testResult.TestCaseResult.StackTrace;
        FailureRate = testResult.FailureRate;
        TestSubResults = testResult.TestCaseResult.SubResults?
            .Where(s => s.Outcome == TestOutcomeValue.Failed)
            .Select(s => new TestSubResultView(s.Name, s.ErrorMessage, s.StackTrace)).ToList()
            ?? [];
        KnownIssues = testResult.KnownIssues;
        ConsoleLogLink = testResult.HelixWorkItem?.ConsoleLogUrl;

        KnownIssueUrlOptions urlOptions = markdownParameters.KnownIssueUrlOptions ?? new KnownIssueUrlOptions();
        ReportKnownIssueInfraTestUrl = GetReportIssueUrl(urlOptions.InfrastructureIssueParameters, urlOptions.Host,
            markdownParameters.Repository.Id, markdownParameters.PullRequest);
        ReportKnownIssueRepositoryTestUrl = GetReportIssueUrl(urlOptions.RepositoryIssueParameters, urlOptions.Host,
            markdownParameters.Repository.Id, markdownParameters.PullRequest);
    }

    private string BuildTestLogsWebUri(TestCaseResult testResult)
    {
        // e.g. https://dev.azure.com/dnceng/public/_build/results?buildId=975143&view=ms.vss-test-web.build-test-results-tab&runId=30605638&resultId=148065&paneView=dotnet-dnceng.dnceng-build-release-tasks.helix-test-information-tab

        return BuildTestResultsTabUri(
            testResult,
            "dotnet-dnceng.dnceng-build-release-tasks.helix-test-information-tab"
        );
    }

    private string BuildTestResultsTabUri(TestCaseResult testResult, string panel)
    {
        string uri = $"{_azDOUrl}/{testResult.ProjectName}/_build/results?buildId={testResult.BuildId}&view=ms.vss-test-web.build-test-results-tab&runId={testResult.TestRunId}&resultId={testResult.Id}";

        if (panel is not null)
        {
            uri += $"&paneView={Uri.EscapeDataString(panel)}";
        }

        return uri;
    }

    private string GetReportIssueUrl(IssueParameters issueParameters, string host, string repository, string pullRequest)
    {
        var parameters = new Dictionary<string, string>
        {
            {"build", BuildUrl ?? ""},
            {"build-leg", TestName},
            {"repository", issueParameters?.Repository ?? repository},
            {"pr", pullRequest ?? "N/A"}
        };

        return KnownIssueHelper.GetReportIssueUrl(parameters, issueParameters, host, repository, pullRequest);
    }
}
