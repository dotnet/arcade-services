// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BuildInsights.BuildAnalysis.Models;
using BuildInsights.BuildAnalysis.Services;
using BuildInsights.KnownIssues.Models;
using Microsoft.Internal.Helix.KnownIssues.Services;
using Microsoft.Internal.Helix.Utility.AzureDevOps.Models;

namespace BuildInsights.BuildAnalysis.Providers;

public class TestResultProvider : ITestResultService
{
    private readonly IHelixDataService _helixDataService;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IKnownIssuesMatchService _knownIssuesMatchService;
    private readonly AzureDevOpsSettingsCollection _azdoSettings;
    private readonly KnownIssuesAnalysisLimits _analysisLimits;
    private readonly ILogger<TestResultProvider> _logger;

    public TestResultProvider(IHelixDataService helixDataService,
        IHttpClientFactory httpFactory,
        IKnownIssuesMatchService knownIssuesMatchService,
        IOptions<AzureDevOpsSettingsCollection> azdoSettings,
        IOptions<KnownIssuesAnalysisLimits> analysisLimits,
        ILogger<TestResultProvider> logger)
    {
        _helixDataService = helixDataService;
        _httpFactory = httpFactory;
        _knownIssuesMatchService = knownIssuesMatchService;
        _azdoSettings = azdoSettings.Value;
        _analysisLimits = analysisLimits.Value;
        _logger = logger;
    }

    public async Task<TestKnownIssuesAnalysis> GetTestFailingWithKnownIssuesAnalysis(
        IReadOnlyList<TestRunDetails> failingTestCaseResults,
        IReadOnlyList<KnownIssue> knownIssues,
        string orgId)
    {
        List<TestResult> testResults = failingTestCaseResults
            .SelectMany(run => run.Results)
            .Select(result => new TestResult(result, _azdoSettings.Settings.First(x => x.OrgId == orgId).CollectionUri))
            .ToList();

        List<TestResult> testResultsToAnalyze = testResults.Take(_analysisLimits.FailingTestCountLimit).ToList();
        HelixLogKnownIssuesAnalysis helixLogKnownIssuesAnalysis = await GetKnownIssuesInHelixConsoleLogs(testResultsToAnalyze,
            knownIssues.Where(k => !k.Options.ExcludeConsoleLog).ToList());

        bool isTestAnalysisComplete = _analysisLimits.FailingTestCountLimit >= testResults.Count && helixLogKnownIssuesAnalysis.AllHelixLogsAnalyzed;

        var testResultsWithKnownIssues = new List<TestResult>();
        foreach (TestResult testResult in testResultsToAnalyze)
        {
            var knownIssueTests = new List<KnownIssue>();
            if (_helixDataService.IsHelixWorkItem(testResult.TestCaseResult.Comment) &&
                helixLogKnownIssuesAnalysis.KnownIssuesByHelixLog.TryGetValue(testResult.TestCaseResult.Comment, out List<KnownIssue> knownIssuesInLog))
            {
                knownIssueTests.AddRange(knownIssuesInLog);
            }

            knownIssueTests.AddRange(_knownIssuesMatchService.GetKnownIssuesInString(testResult.TestCaseResult.ErrorMessage, knownIssues));
            knownIssueTests.AddRange(_knownIssuesMatchService.GetKnownIssuesInString(testResult.TestCaseResult.StackTrace, knownIssues));

            testResult.KnownIssues = knownIssueTests.Distinct().ToImmutableList();

            if (knownIssueTests.Count > 0) testResultsWithKnownIssues.Add(testResult);
        }

        _logger.LogInformation(
            $"Test known issues summary: Tests analyzed: {testResultsToAnalyze.Count} / Helix Logs Analyzed: {helixLogKnownIssuesAnalysis.KnownIssuesByHelixLog.Count} / Complete Analysis: {isTestAnalysisComplete}");
        _logger.LogInformation($"Count of known issues found on tests: {testResultsWithKnownIssues.Count}");

        return new TestKnownIssuesAnalysis(isTestAnalysisComplete, testResultsWithKnownIssues);
    }

    private async Task<HelixLogKnownIssuesAnalysis> GetKnownIssuesInHelixConsoleLogs(List<TestResult> testResultsToBeAnalyzed, IReadOnlyList<KnownIssue> knownIssues)
    {
        List<string> helixLogFilesComments = testResultsToBeAnalyzed.Select(t => t.TestCaseResult.Comment)
            .Where(h => _helixDataService.IsHelixWorkItem(h)).Distinct().ToList();

        Dictionary<string, List<HelixWorkItem>> testHelixWorkItems = await _helixDataService.TryGetHelixWorkItems(
            helixLogFilesComments.Take(_analysisLimits.HelixLogsFilesLimit).ToImmutableList(),
            CancellationToken.None);

        var knownIssuesInHelixLogs = new Dictionary<string, List<KnownIssue>>();
        foreach (KeyValuePair<string, List<HelixWorkItem>> testHelixWorkItem in testHelixWorkItems)
        {
            var knownIssueTests = new List<KnownIssue>();
            foreach (HelixWorkItem helixWorkItem in testHelixWorkItem.Value)
            {
                await using Stream httpStream = await _httpFactory.CreateClient(GetType().Name)
                    .GetStreamAsync(helixWorkItem.ConsoleLogUrl);

                knownIssueTests.AddRange(await _knownIssuesMatchService.GetKnownIssuesInStream(httpStream, knownIssues));
            }

            knownIssuesInHelixLogs.Add(testHelixWorkItem.Key, knownIssueTests.Distinct().ToList());

        }

        return new HelixLogKnownIssuesAnalysis
        {
            AllHelixLogsAnalyzed = helixLogFilesComments.Count < _analysisLimits.HelixLogsFilesLimit,
            KnownIssuesByHelixLog = knownIssuesInHelixLogs
        };
    }
}

public struct HelixLogKnownIssuesAnalysis
{
    public bool AllHelixLogsAnalyzed { get; set; }
    public Dictionary<string, List<KnownIssue>> KnownIssuesByHelixLog { get; set; }
}
