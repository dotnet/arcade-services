// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using System.Text.Json;
using BuildInsights.BuildAnalysis;
using BuildInsights.BuildAnalysis.Models;
using BuildInsights.Data.Models;
using BuildInsights.GitHub;
using BuildInsights.GitHub.Models;
using BuildInsights.KnownIssues;
using BuildInsights.KnownIssues.Models;
using BuildInsights.KnownIssues.WorkItems;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildInsights.KnownIssuesProcessor;

public class KnownIssuesMessageHandler : IQueueMessageHandler
{
    private readonly IRequestAnalysisService _buildAnaylysisRequestProvider;
    private readonly IOptionsMonitor<KnownIssuesProcessorOptions> _options;
    private readonly ILogger<KnownIssuesMessageHandler> _logger;
    private readonly IBuildDataService _buildDataService;
    private readonly IKnownIssuesHistoryService _knownIssuesHistoryService;
    private readonly IBuildAnalysisHistoryService _buildAnalysisHistoryService;
    private readonly IGitHubChecksService _gitHubChecksService;
    private readonly IGitHubIssuesService _issuesService;
    private readonly IBuildProcessingStatusService _processingStatusService;
    private readonly ISystemClock _clock;

    public KnownIssuesMessageHandler(
        IOptionsMonitor<KnownIssuesProcessorOptions> options,
        IBuildDataService buildDataService,
        IRequestAnalysisService buildRequestService,
        IKnownIssuesHistoryService knownIssuesHistoryService,
        IBuildAnalysisHistoryService buildAnalysisHistoryService,
        IGitHubChecksService gitHubChecksService,
        IGitHubIssuesService gitHubIssuesService,
        IBuildProcessingStatusService processingStatusService,
        ISystemClock systemClock,
        ILogger<KnownIssuesMessageHandler> logger)
    {
        _options = options;
        _buildAnaylysisRequestProvider = buildRequestService;
        _buildDataService = buildDataService;
        _knownIssuesHistoryService = knownIssuesHistoryService;
        _buildAnalysisHistoryService = buildAnalysisHistoryService;
        _gitHubChecksService = gitHubChecksService;
        _issuesService = gitHubIssuesService;
        _processingStatusService = processingStatusService;
        _clock = systemClock;
        _logger = logger;
    }

    public async Task HandleMessageAsync(IQueuedWork message, bool isLastAttempt, CancellationToken cancellationToken)
    {
        string messageString = await message.GetStringAsync();

        var processRequest = JsonSerializer.Deserialize<AnalysisProcessRequest>(messageString)!;

        GitHubIssue issue = await _gitHubChecksService.GetIssueAsync(processRequest.Repository, processRequest.IssueId);
        KnownIssueJson issueJson = KnownIssueHelper.GetKnownIssueJson(issue.Body);

        if (issueJson.ErrorMessage is null or { Count: 0 } && issueJson.ErrorPattern is null or { Count: 0 })
        {
            if (issueJson.ErrorMessage == null && issueJson.ErrorPattern == null &&
                (!issue.Body?.Contains(KnownIssueHelper.ErrorMessageTemplateIdentifier) ?? true))
            {
                await AddErrorMessageTemplate(issue);
            }

            _logger.LogInformation("Failed to find an error message in issue {issueRepo}#{issueId}", issue.Repository, issue.Id);
        }
        else
        {
            KnownIssueError? latestKnownIssueError = await _knownIssuesHistoryService.GetLatestKnownIssueError(issue.RepositoryWithOwner, issue.Id, cancellationToken);
            if (latestKnownIssueError?.ErrorMessage?.Equals(issueJson.ErrorMessage) ?? false)
            {
                _logger.LogInformation("The error message: {errorMessage} from github {issueRepository}#{issueId} has been already processed. Skipping it", issueJson.ErrorMessage, issue.RepositoryWithOwner, issue.Id);
                return;
            }

            // Filter builds, all builds for infra issues
            // In staging, RepositoryIssuesOnly will be true and the scope of infrastructure issues will be limited to the test repo
            string repositoryFilter = null;
            if (!issue.RepositoryWithOwner.Equals(_options.CurrentValue.KnownIssuesRepo) || _options.CurrentValue.RepositoryIssuesOnly)
                repositoryFilter = issue.RepositoryWithOwner;

            _logger.LogInformation("Reprocessing {issueType} issue {issueRepo}#{issueNumber}",
                repositoryFilter == null ? "infrastructure" : "repository", issue.RepositoryWithOwner, issue.Id);

            DateTimeOffset dateTimeOFilter = _clock.UtcNow.AddDays(-2);
            List<KnownIssueAnalysis> analysisList = await _knownIssuesHistoryService.GetKnownIssuesHistory(issue.RepositoryWithOwner, issue.Id,
                    dateTimeOFilter, cancellationToken);
            IReadOnlyList<int> analyzedBuildIds = analysisList.Where(knownIssueAnalysis =>
                     knownIssueAnalysis.ErrorMessage.Equals(KnownIssueHelper.GetKnownIssueErrorMessageStringConversion(issueJson.ErrorMessage)) ||
                     knownIssueAnalysis.ErrorMessage.Equals(KnownIssueHelper.GetKnownIssueErrorMessageStringConversion(issueJson.ErrorPattern)))
                .Select(a => int.Parse(a.RowKey)).ToList();

            List<BuildAnalysisEvent> buildsEventWithRepositoryNotSupported = await _buildAnalysisHistoryService.GetBuildsWithRepositoryNotSupported(dateTimeOFilter, cancellationToken);
            IReadOnlyList<int> buildsWithRepositoryNotSupported = buildsEventWithRepositoryNotSupported.Select(b => int.Parse(b.RowKey)).ToList();

            List<BuildProcessingStatusEvent> buildEventsWithOverrideForConclusion = await _processingStatusService.GetBuildsWithOverrideConclusion(dateTimeOFilter, cancellationToken);
            IReadOnlyList<int> buildWithOverrideForConclusion = buildEventsWithOverrideForConclusion.Select(b => int.Parse(b.RowKey)).ToList();

            foreach (AzureDevOpsProjects project in _options.CurrentValue.AzureDevOpsProjects)
            {
                string repositoryFilterForProject = project.IsInternal ? GetAzureRepoName(repositoryFilter) : repositoryFilter;
                IReadOnlyList<Build> buildList = await _buildDataService.GetFailedBuildsAsync(project.OrgId, project.ProjectId, repositoryFilterForProject, cancellationToken);

                buildList = buildList.Where(b => !analyzedBuildIds.Contains(b.Id) && !buildsWithRepositoryNotSupported.Contains(b.Id) && !buildWithOverrideForConclusion.Contains(b.Id)).ToList();

                _logger.LogInformation("Reprocessing {buildCount} builds for issue: {issueRepo}#{issueNumber} on {project}",
                    buildList.Count, issue.RepositoryWithOwner, issue.Id, project);
                await _buildAnaylysisRequestProvider.RequestAnalysisAsync(buildList);
            }
        }

        await _knownIssuesHistoryService.SaveKnownIssueError(issue.RepositoryWithOwner, issue.Id, issueJson.ErrorMessage,
            cancellationToken);
    }

    private async Task AddErrorMessageTemplate(GitHubIssue issue)
    {
        string errorMessageTemplate = $@"
{KnownIssueHelper.ErrorMessageTemplateIdentifier}
### Known Issue Error Message
{KnownIssueHelper.GetKnownIssueSectionTemplate()}
";
        var bodyWithReport = new StringBuilder();
        bodyWithReport.Append(issue.Body);
        bodyWithReport.Append(errorMessageTemplate);
        await _issuesService.UpdateIssueBodyAsync(issue.RepositoryWithOwner, issue.Id, bodyWithReport.ToString());
    }

    private string GetAzureRepoName(string repositoryWithOwner)
    {
        return string.IsNullOrEmpty(repositoryWithOwner) ? repositoryWithOwner : repositoryWithOwner.Replace("/", "-");
    }
}
