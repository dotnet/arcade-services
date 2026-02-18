// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using System.Text;
using BuildInsights.GitHub.Models;
using BuildInsights.GitHubGraphQL;
using BuildInsights.GitHubGraphQL.GitHubGraphQLAPI;
using BuildInsights.KnownIssues.Models;
using Maestro.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildInsights.KnownIssues;

public interface IKnownIssueReporter
{
    Task RunAsync();
}

public class KnownIssueReporter : IKnownIssueReporter
{
    private const string InternalProject = "internal";

    private readonly IGitHubGraphQLClient _graphQlClient;
    private readonly IGitHubIssuesService _issuesService;
    private readonly IOptions<KnownIssuesProjectOptions> _knownIssuesProjectOptions;
    private readonly IKnownIssuesService _knownIssuesService;
    private readonly IEnumerable<string> _knownIssuesLabels;
    private readonly KnownIssuesReportHelper _knownIssueReportHelper;
    private readonly SsaCriteriaSettings _ssaCriteriaSettings;
    private readonly ILogger<KnownIssueReporter> _logger;

    public KnownIssueReporter(
        IGitHubGraphQLClient graphQlClient,
        IGitHubIssuesService issuesService,
        IKnownIssuesService knownIssuesService,
        KnownIssuesReportHelper knownIssuesReportHelper,
        IOptions<KnownIssuesProjectOptions> knownIssuesProjectOptions,
        IOptions<GitHubIssuesSettings> gitHubIssuesSettings,
        IOptions<SsaCriteriaSettings> ssaCriteriaSettings,
        ILogger<KnownIssueReporter> logger)
    {
        _graphQlClient = graphQlClient;
        _issuesService = issuesService;
        _knownIssuesService = knownIssuesService;
        _knownIssuesProjectOptions = knownIssuesProjectOptions;
        _knownIssuesLabels = gitHubIssuesSettings.Value.KnownIssuesLabels;
        _ssaCriteriaSettings = ssaCriteriaSettings.Value;
        _knownIssueReportHelper = knownIssuesReportHelper;
        _logger = logger;
    }

    public async Task RunAsync()
    {
        ImmutableList<GitHubIssue> issues = await GetKnownIssues();
        _logger.LogInformation("Execute known issues report for {issueCount} issues", issues.Count);

        foreach (GitHubIssue issue in issues)
        {
            try
            {
                _logger.LogInformation("Executing known issues report for {repository}#{issue} issues", issue.RepositoryWithOwner, issue.Id);
                ImmutableList<TestKnownIssueMatch> testKnownIssues = await _knownIssuesService.GetTestKnownIssuesMatchesForIssue(issue.Id, issue.RepositoryWithOwner);
                ImmutableList<KnownIssueMatch> buildKnownIssuesMatches = await _knownIssuesService.GetKnownIssuesMatchesForIssue(issue.Id, issue.RepositoryWithOwner);
                ImmutableList<KnownIssueMatch> filteredBuildMatches = buildKnownIssuesMatches.RemoveAll(buildMatch =>
                    testKnownIssues.Any(t =>
                        t.IssueId == buildMatch.IssueId &&
                        t.IssueRepository == buildMatch.IssueRepository &&
                        t.BuildId == buildMatch.BuildId &&
                        t.BuildRepository == buildMatch.BuildRepository));
                string report = WriteReport(filteredBuildMatches, testKnownIssues);
                string updatedBody = GetIssueBodyWithNewReport(issue.Body, report);
                await UpdateIssueWithNewReport(issue.RepositoryWithOwner, issue.Id, updatedBody);
                await TrySendKnownIssueToSsa(issue, filteredBuildMatches, testKnownIssues);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to update Known issue report for issue {repository}#{issueId}", issue.RepositoryWithOwner, issue.Id);
            }

        }
    }

    public async Task<ImmutableList<GitHubIssue>> GetKnownIssues()
    {
        List<GitHubGraphQLProjectV2Item> boardProject = await _graphQlClient.GetAllProjectIssues(
            _knownIssuesProjectOptions.Value.Organization,
            _knownIssuesProjectOptions.Value.ProjectNumber);
        IEnumerable<GitHubIssue> issuesBoardProject = boardProject.Where(i => !i.Content.Closed).Select(MapModel);

        return issuesBoardProject.Where(issue =>
                issue.Labels.Any(label => _knownIssuesLabels.Any(k => k.Equals(label, StringComparison.OrdinalIgnoreCase))))
            .ToImmutableList();
    }

    public string WriteReport(ImmutableList<KnownIssueMatch> knownIssueMatches, ImmutableList<TestKnownIssueMatch> testKnownIssueMatches)
    {
        const int limitOfRecordsReported = 100;

        var report = new StringBuilder();
        report.AppendLine(KnownIssueHelper.StartKnownIssueReportIdentifier);
        report.AppendLine();
        report.AppendLine("### Report");

        if (knownIssueMatches.Count > 0)
        {
            report.AppendLine("|Build|Definition|Step Name|Console log|Pull Request|");
            report.AppendLine("|---|---|---|---|---|");
            foreach (KnownIssueMatch knownIssue in knownIssueMatches.OrderByDescending(k => k.StepStartTime).Take(limitOfRecordsReported))
            {
                report.AppendLine(
                    $"|{GetBuildLink(knownIssue)}|{knownIssue.BuildRepository}|{knownIssue.StepName}|{GetLogLink(knownIssue.LogURL)}|{GetPullRequestLink(knownIssue.BuildRepository, knownIssue.PullRequest, knownIssue.Project, knownIssue.Organization)}|");
            }

            if (knownIssueMatches.Count > limitOfRecordsReported)
            {
                report.AppendLine($"Displaying {limitOfRecordsReported} of {knownIssueMatches.Count} results");
            }
        }

        if (testKnownIssueMatches.Count > 0)
        {
            report.AppendLine();
            report.AppendLine("|Build|Definition|Test|Pull Request|");
            report.AppendLine("|---|---|---|---|");
            foreach (TestKnownIssueMatch testKnownIssue in testKnownIssueMatches.OrderByDescending(k => k.CompletedDate).Take(limitOfRecordsReported))
            {
                report.AppendLine(
                    $"|{GetBuildLink(testKnownIssue)}|{testKnownIssue.BuildRepository}|{GetTestLink(testKnownIssue)}|{GetPullRequestLink(testKnownIssue.BuildRepository, testKnownIssue.PullRequest, testKnownIssue.Project, testKnownIssue.Organization)}|");
            }

            if (testKnownIssueMatches.Count > limitOfRecordsReported)
            {
                report.AppendLine($"Displaying {limitOfRecordsReported} of {testKnownIssueMatches.Count} results");
            }
        }

        KnownIssuesHits knownIssuesHits = _knownIssueReportHelper.GetIssuesHits(knownIssueMatches);
        KnownIssuesHits testKnownIssuesHits = _knownIssueReportHelper.GetIssuesHits(testKnownIssueMatches);
        report.AppendLine("#### Summary");
        report.AppendLine("|24-Hour Hit Count|7-Day Hit Count|1-Month Count|");
        report.AppendLine("|---|---|---|");
        report.AppendLine($"|{knownIssuesHits.Daily + testKnownIssuesHits.Daily}|{knownIssuesHits.Weekly + testKnownIssuesHits.Weekly}|{knownIssuesHits.Monthly + testKnownIssuesHits.Monthly}|");
        report.Append(KnownIssueHelper.EndKnownIssueReportIdentifier);

        return report.ToString();
    }

    public async Task TrySendKnownIssueToSsa(GitHubIssue issue,
        ImmutableList<KnownIssueMatch> buildKnownIssuesMatches,
        ImmutableList<TestKnownIssueMatch> testKnownIssuesMatches)
    {
        KnownIssuesHits knownIssuesHits = _knownIssueReportHelper.GetIssuesHits(buildKnownIssuesMatches);
        KnownIssuesHits testKnownIssuesHits = _knownIssueReportHelper.GetIssuesHits(testKnownIssuesMatches);

        int dailyHits = knownIssuesHits.Daily + testKnownIssuesHits.Daily;
        if (dailyHits >= _ssaCriteriaSettings.DailyHitsForEscalation &&
            _ssaCriteriaSettings.SsaRepositories.Contains(issue.RepositoryWithOwner) &&
            !issue.Labels.Contains(_ssaCriteriaSettings.SsaLabel))
        {
            await _issuesService.AddLabelToIssueAsync(issue.RepositoryWithOwner, issue.Id, _ssaCriteriaSettings.SsaLabel);

            _logger.LogInformation("The issue {repository}#{issueId} has been added to SSA, with {dailyHits} daily hits",
                issue.RepositoryWithOwner, issue.Id, dailyHits);
        }
    }

    private static string GetLogLink(string logURL)
    {
        return string.IsNullOrEmpty(logURL) ? string.Empty : $"[Log]({logURL})";
    }

    private static string GetPullRequestLink(string repository, string pullRequestNumber, string project, string organization)
    {
        if (project == InternalProject)
        {
            if (string.IsNullOrEmpty(organization) || string.IsNullOrEmpty(project) ||
                string.IsNullOrEmpty(pullRequestNumber) || string.IsNullOrEmpty(repository))
            {
                return string.Empty;
            }

            string link = $"https://dev.azure.com/{organization}/{project}/_git/{repository}/pullrequest/{pullRequestNumber}";
            return $"[#{pullRequestNumber}]({link})";
        }

        return string.IsNullOrEmpty(repository) || string.IsNullOrEmpty(pullRequestNumber) ? string.Empty : $"{repository}#{pullRequestNumber}";
    }

    public static string GetBuildLink(KnownIssueMatch knownIssue)
    {
        if (!string.IsNullOrEmpty(knownIssue.Project))
        {
            string organization = !string.IsNullOrEmpty(knownIssue.Organization)
                ? knownIssue.Organization
                : BuildUrlUtils.ParseOrganizationFromBuildUrl(knownIssue.LogURL);
            string link = $"https://dev.azure.com/{organization}/{knownIssue.Project}/_build/results?buildId={knownIssue.BuildId}";

            return $"[{knownIssue.BuildId}]({link})";
        }
        else
            return knownIssue.BuildId.ToString();
    }

    private static string GetBuildLink(TestKnownIssueMatch testKnownIssue)
    {
        if (string.IsNullOrEmpty(testKnownIssue.Project))
        {
            return testKnownIssue.BuildId.ToString();
        }

        string organization = !string.IsNullOrEmpty(testKnownIssue.Organization)
            ? testKnownIssue.Organization
            : BuildUrlUtils.ParseOrganizationFromBuildUrl(testKnownIssue.Url);
        string link = $"https://dev.azure.com/{organization}/{testKnownIssue.Project}/_build/results?buildId={testKnownIssue.BuildId}";
        return $"[{testKnownIssue.BuildId}]({link})";
    }

    private static string GetTestLink(TestKnownIssueMatch testKnownIssue)
    {
        return string.IsNullOrEmpty(testKnownIssue.Url) ? testKnownIssue.TestResultName : $"[{testKnownIssue.TestResultName}]({testKnownIssue.Url})";
    }

    public static string GetIssueBodyWithNewReport(string body, string report)
    {
        if (string.IsNullOrEmpty(body)) return report;

        int startIndex = body.IndexOf(KnownIssueHelper.StartKnownIssueReportIdentifier, StringComparison.OrdinalIgnoreCase);
        int endIndex = body.LastIndexOf(KnownIssueHelper.EndKnownIssueReportIdentifier, StringComparison.OrdinalIgnoreCase);
        startIndex = startIndex == -1 ? body.Length : startIndex;
        endIndex = endIndex == -1 ? body.Length : endIndex + KnownIssueHelper.EndKnownIssueReportIdentifier.Length;

        string prevMessage = body[..startIndex];
        string endMessage = body[endIndex..];

        var bodyWithReport = new StringBuilder();
        bodyWithReport.Append(prevMessage);
        bodyWithReport.Append(report);
        bodyWithReport.Append(endMessage);

        return bodyWithReport.ToString();
    }

    public async Task UpdateIssueWithNewReport(string repository, int issueNumber, string updatedBody)
    {
        _logger.LogInformation("Updating known issues report for {repository}#{issue} issues", repository, issueNumber);
        await _issuesService.UpdateIssueBodyAsync(repository, issueNumber, updatedBody);
    }

    private static GitHubIssue MapModel(GitHubGraphQLProjectV2Item item)
    {
        return new GitHubIssue(
            item.Content.Number,
            item.Content.Title,
            item.Content.Repository.Name,
            item.Content.Repository.NameWithOwner,
            item.Content.Body,
            item.Content.Url,
            item.Content.Labels?.Nodes?.Select(n => n.Name).ToList() ?? []);
    }
}
