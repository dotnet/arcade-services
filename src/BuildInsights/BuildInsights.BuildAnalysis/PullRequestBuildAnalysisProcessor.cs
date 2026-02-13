// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using Microsoft.ApplicationInsights;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BuildInsights.BuildAnalysis.Models;
using BuildInsights.GitHub.Models;
using BuildInsights.QueueInsights.Models;
using BuildInsights.GitHub;
using BuildInsights.QueueInsights;

namespace BuildInsights.BuildAnalysis;

public interface IPullRequestService
{
    bool IsPullRequestMessage(string message, out PullRequestData pullRequestData);
    Task ProcessPullRequestMessage(PullRequestData pullRequestData);
}

public class PullRequestBuildAnalysisProcessor : IPullRequestService
{
    private readonly IGitHubChecksService _gitHubChecksService;
    private readonly IInstallationLookup _installationLookup;
    private readonly IMarkdownGenerator _markdownGenerator;
    private readonly IQueueInsightsMarkdownGenerator _qiMarkdownGenerator;
    private readonly IOptions<QueueInsightsBetaSettings> _qiSettings;
    private readonly TelemetryClient _telemetry;
    private readonly ILogger<PullRequestBuildAnalysisProcessor> _logger;

    public PullRequestBuildAnalysisProcessor(IGitHubChecksService gitHubChecksService,
        IInstallationLookup installationLookup,
        IMarkdownGenerator markdownGenerator,
        IQueueInsightsMarkdownGenerator qiMarkdownGenerator,
        IOptions<QueueInsightsBetaSettings> qiSettings,
        TelemetryClient telemetry,
        ILogger<PullRequestBuildAnalysisProcessor> logger)
    {
        _gitHubChecksService = gitHubChecksService;
        _installationLookup = installationLookup;
        _markdownGenerator = markdownGenerator;
        _qiMarkdownGenerator = qiMarkdownGenerator;
        _qiSettings = qiSettings;
        _telemetry = telemetry;
        _logger = logger;
    }

    public bool IsPullRequestMessage(string message, out PullRequestData pullRequestData)
    {
        try
        {
            pullRequestData = JsonSerializer.Deserialize<PullRequestData>(message);
            return pullRequestData != null && !string.IsNullOrEmpty(pullRequestData.Action);
        }
        catch
        {
            pullRequestData = null;
            return false;
        }
    }

    public async Task ProcessPullRequestMessage(PullRequestData pullRequestData)
    {
        switch (pullRequestData?.Action)
        {
            case "opened":
            case "synchronize":
            case "reopened":
                await CreateNewCheckRun(pullRequestData);
                await CreateQueueInsightsCheck(pullRequestData);
                break;
            case "closed":
                await RecordMergedPullRequestMetrics(pullRequestData);
                break;
            default:
                _logger.LogError("Unexpected action from Pull Request {action}, aborting", pullRequestData?.Action);
                return;
        }
    }

    private async Task RecordMergedPullRequestMetrics(PullRequestData pullRequest)
    {
        if (!pullRequest.Merged) return;

        _logger.LogInformation("Processing merged pull request for organization: {organization}, repository: {repository}, commit: {commit}, pr: {pr} ",
            pullRequest.Organization, pullRequest.Repository, pullRequest.HeadSha, pullRequest.Number);

        string repo = $"{pullRequest.Organization}/{pullRequest.Repository}";
        IEnumerable<CheckRun> checkRuns = await _gitHubChecksService.GetAllCheckRunsAsync(repo, pullRequest.HeadSha);

        int failedCount = 0;
        int successCount = 0;
        int pendingCount = 0;
        int otherCount = 0;

        foreach (CheckRun checkRun in checkRuns)
        {
            _telemetry.TrackEvent(
                "CheckRunConclusion",
                new Dictionary<string, string>
                {
                    {"organization", pullRequest.Organization},
                    {"repository", pullRequest.Repository},
                    {"pullRequestNumber", pullRequest.Number.ToString()},
                    {"buildId", checkRun.AzureDevOpsBuildId.ToString()},
                    {"checkName", checkRun.Name},
                    {"conclusion", checkRun.Conclusion.ToString()}
                }
            );

            switch (checkRun.Conclusion)
            {
                case CheckConclusion.Failure:
                    failedCount++;
                    break;
                case CheckConclusion.Success:
                    successCount++;
                    break;
                case CheckConclusion.Pending:
                    pendingCount++;
                    break;
                default:
                    otherCount++;
                    break;
            }
        }

        _telemetry.TrackEvent(
            "CheckRunConclusionAggregate",
            new Dictionary<string, string>
            {
                {"organization", pullRequest.Organization},
                {"repository", pullRequest.Repository},
                {"pullRequestNumber", pullRequest.Number.ToString()},
                {"overall", failedCount > 0 ? "failed" : "success"},
                {"commitHash", pullRequest.HeadSha}
            },
            new Dictionary<string, double>
            {
                {"failed", failedCount},
                {"success", successCount},
                {"pending", pendingCount},
                {"other", otherCount}
            }
        );
    }

    private async Task CreateNewCheckRun(PullRequestData pullRequest)
    {
        _logger.LogInformation("Starting new check run for {organization} repository: {repository}, commit: {commit}, pr: {pr} ",
            pullRequest.Organization, pullRequest.Repository, pullRequest.HeadSha, pullRequest.Number.ToString());

        if (!await _installationLookup.IsOrganizationSupported(pullRequest.Organization))
        {
            _logger.LogInformation("Disallowed organization detected: {gitHubOrg}", pullRequest.Organization);
            string markdown = ":x: This application is not intended for public use, uninstall from this organization or contact an admin for permission :x:";

            await _gitHubChecksService.PostChecksResultAsync("Build Analysis", ".NET Result Analysis", markdown,
                pullRequest.Repository, pullRequest.HeadSha, CheckResult.Failed, CancellationToken.None);
            return;
        }

        string repository = $"{pullRequest.Organization}/{pullRequest.Repository}";
        if (!await _gitHubChecksService.IsRepositorySupported(repository))
        {
            _logger.LogInformation("Repository '{repository}' is not supported", repository);
            return;
        }

        string preliminaryMarkdown = _markdownGenerator.GenerateEmptyMarkdown(
            new Models.UserSentimentParameters
            {
                Repository = repository,
                CommitHash = pullRequest.HeadSha,
                IsEmpty = true
            });

        _logger.LogInformation("Creating check run for repository {repository} for commit {commit}", repository, pullRequest.HeadSha);
        long checkId = await _gitHubChecksService.PostChecksResultAsync("Build Analysis", ".NET Result Analysis",
            preliminaryMarkdown, repository, pullRequest.HeadSha, CheckResult.InProgress, CancellationToken.None);

        _logger.LogInformation("New check run with id:{checkId} created for {organization} repository: {repository}, commit: {commit}, pr: {pr} ",
            checkId, pullRequest.Organization, pullRequest.Repository, pullRequest.HeadSha, pullRequest.Number.ToString());

        _telemetry.TrackEvent(
            "CheckSuiteRequested",
            new Dictionary<string, string>
            {
                {"commitHash", pullRequest.HeadSha},
                {"repository", repository},
                {"pullRequestNumber", pullRequest.Number.ToString()}
            }
        );
    }

    private async Task CreateQueueInsightsCheck(PullRequestData pullRequestData)
    {
        string repo = $"{pullRequestData.Organization}/{pullRequestData.Repository}";

        if (!_qiSettings.Value.AllowedRepos.Contains(repo))
        {
            _logger.LogInformation("Not showing pending queue insights for repo: {repo}.", repo);
            return;
        }

        _logger.LogInformation("Creating pending Queue Insights check for repo: {repo} pr: {pr}", repo, pullRequestData.Number);

        string markdown = _qiMarkdownGenerator.GeneratePendingMarkdown(repo, pullRequestData.HeadSha, pullRequestData.Number.ToString());

        long id = await _gitHubChecksService.PostChecksResultAsync(QueueInsightsService.CheckRunName, QueueInsightsService.CheckRunResultsName, markdown,
            repo, pullRequestData.HeadSha, CheckResult.InProgress, CancellationToken.None);

        _logger.LogInformation("Created Pending Queue Insights check id: {id} repo: {repo} pr: {pr}", id, repo, pullRequestData.Number);
    }
}
