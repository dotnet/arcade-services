// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using BuildInsights.GitHub;
using BuildInsights.GitHub.Models;
using BuildInsights.QueueInsights.Models;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Internal.Helix.Machines.MatrixOfTruthOutputDeserialization.V1.Models;

namespace BuildInsights.QueueInsights;

/// <summary>
///     Provides insight and visibility into the current state of Helix queues by posting a Check Run.
/// </summary>
public interface IQueueInsightsService
{
    /// <summary>
    ///     Creates the queue insights for a specific repo and its commit hash.
    /// </summary>
    /// <param name="repo">The repository to create the queue insights for.</param>
    /// <param name="commitHash">The SHA hash of the git commit.</param>
    /// <param name="pullRequest">The pull request number.</param>
    /// <param name="pipelineIds">The IDs of pipelines the PR uses.</param>
    /// <param name="targetBranch">The target branch the PR is merging into.</param>
    /// <param name="criticalIssues"><c>true</c> if there are any critical infrastructure issues.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The id of the new check run created in the GitHub pull request.</returns>
    public Task<long> CreateQueueInsightsAsync(string repo, string commitHash, string pullRequest,
        IImmutableSet<int> pipelineIds, string targetBranch, bool criticalIssues, CancellationToken cancellationToken);
}

public class QueueInsightsService : IQueueInsightsService
{
    public static readonly string CheckRunName = "Helix Queue Insights (preview)";
    public static readonly string CheckRunResultsName = "View the current status of Helix";
    private const int HighestWaitTimeCount = 5;
    private readonly IGitHubChecksService _gitHubChecksService;
    private readonly ILogger<QueueInsightsService> _logger;
    private readonly IMatrixOfTruthService _matrixOfTruth;
    private readonly IQueueInsightsMarkdownGenerator _queueInsightsMarkdownGenerator;
    private readonly IQueueTimeService _queueTimeService;
    private readonly IOptions<QueueInsightsBetaSettings> _settings;
    private readonly TelemetryClient _telemetry;

    public QueueInsightsService(IGitHubChecksService gitHubChecksService,
        IQueueTimeService queueTimeService,
        IQueueInsightsMarkdownGenerator queueInsightsMarkdownGenerator,
        IOptions<QueueInsightsBetaSettings> settings,
        ILogger<QueueInsightsService> logger,
        TelemetryClient telemetry,
        IMatrixOfTruthService matrixOfTruth)
    {
        _gitHubChecksService = gitHubChecksService;
        _queueTimeService = queueTimeService;
        _queueInsightsMarkdownGenerator = queueInsightsMarkdownGenerator;
        _settings = settings;
        _logger = logger;
        _telemetry = telemetry;
        _matrixOfTruth = matrixOfTruth;
    }

    public async Task<long> CreateQueueInsightsAsync(string repo, string commitHash, string pullRequest,
        IImmutableSet<int> pipelineIds, string targetBranch, bool criticalIssues, CancellationToken cancellationToken)
    {
        if (!_settings.Value.AllowedRepos.Contains(repo))
        {
            _logger.LogInformation("Not showing queue insights for repo: {repo}.", repo);
            return -1;
        }

        _logger.LogInformation("Creating queue insights for repo: {repo} hash: {hash}, PR: {pr}.", repo, commitHash,
            pullRequest);

        IList<PipelineOutputModel> pipelineOutputs = await _matrixOfTruth.GetPipelineOutputsAsync();
        List<PipelineOutputModel> queues = GetPipelineOutputsByBranch(targetBranch, pipelineOutputs)
            .GetHelixQueues(repo)
            .InPipelines(pipelineIds)
            .ToList();

        QueueInsightsResult<ImmutableList<HighestWaitTimeQueueInfoView>> highWaitTimeQueues = null;

        _logger.LogInformation("Found {numQueues} queues", queues.Count);

        QueueInsightsResult<Dictionary<string, TimeSpan>> queueWaitTimes =
            await _queueTimeService.GetWorkItemWaitTimesAsync([.. queues.Select(x => x.EnvironmentName)]);

        if (queueWaitTimes.Result is { Count: > 0 })
            highWaitTimeQueues = await GetHighestQueueWaitsAsync(queueWaitTimes.Result);

        List<PipelineOutputModel> publicBuildInfos = GetPipelineOutputsByBranch(targetBranch, pipelineOutputs)
                .BuildMachines()
                .InPipelines(pipelineIds)
                .ToList();

        _telemetry.TrackEvent("QueueInsightsData",
            new Dictionary<string, string>
            {
                { "repo", repo }
            },
            new Dictionary<string, double>
            {
                { "pipelines", pipelineIds.Count },
                { "queues", queues.Count },
                { "buildMachines", publicBuildInfos.Count }
            });

        var vmQueues = queues
            .Where(x => !x.IsOnPrem())
            .Select(x => x.EnvironmentName)
            .OrderBy(x => x)
            .ToImmutableList();

        var onPremQueues = queues
            .Where(x => x.IsOnPrem())
            .Select(x => x.EnvironmentName)
            .OrderBy(x => x)
            .ToImmutableList();

        var failedQueues = queueWaitTimes.FailedQueues
            .Concat(highWaitTimeQueues?.FailedQueues ?? Enumerable.Empty<string>())
            .OrderBy(x => x)
            .ToImmutableList();

        var microsoftPools = publicBuildInfos
            .Where(x => x.EnvironmentType == BuildPool.MicrosoftHosted)
            .Select(x => x.EnvironmentName)
            .Distinct()
            .ToImmutableList();

        var oneEsHostedPools = publicBuildInfos
            .Where(x => x.EnvironmentType == BuildPool.OneESHosted)
            .Select(x => x.EnvironmentName)
            .Distinct()
            .ToImmutableList();

        var matchingPipelineIdsFromChecks =
            (await _gitHubChecksService.GetBuildCheckRunsAsync(repo, commitHash))
            .Select(x => x.AzureDevOpsPipelineId)
            .ToImmutableHashSet();

        var estimatedPipelineDurations = await _queueTimeService.GetEstimatedPipelineDurations(
            matchingPipelineIdsFromChecks, targetBranch);

        var view = new MarkdownView(highWaitTimeQueues?.Result, repo, commitHash, pullRequest, vmQueues,
            onPremQueues, failedQueues, microsoftPools, oneEsHostedPools, estimatedPipelineDurations,
            criticalIssues);

        string markdown = _queueInsightsMarkdownGenerator.GenerateMarkdown(view);

        return await _gitHubChecksService.PostChecksResultAsync(CheckRunName, CheckRunResultsName, markdown, repo,
            commitHash, CheckResult.Passed, cancellationToken);
    }

    public static IEnumerable<PipelineOutputModel> GetPipelineOutputsByBranch(string targetBranch,
        IEnumerable<PipelineOutputModel> pipelineOutputs)
    {
        return pipelineOutputs
            .Where(x => x.Project == "public")
            .Where(x => x.Branch != null && x.Branch.Equals(targetBranch, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<QueueInsightsResult<ImmutableList<HighestWaitTimeQueueInfoView>>> GetHighestQueueWaitsAsync(
        Dictionary<string, TimeSpan> queueWaitTimes)
    {
        var highWaitTimeQueues = new List<HighestWaitTimeQueueInfoView>(HighestWaitTimeCount);
        var failedQueues = new List<string>();

        foreach ((string queue, TimeSpan waitTime) in queueWaitTimes.OrderByDescending(x => x.Value)
                     .Take(HighestWaitTimeCount))
        {
            TimeSpan? movingAvgWaitTime = await _queueTimeService.GetWorkItemWaitTimeMovingAverageAsync(queue);

            if (movingAvgWaitTime == null)
            {
                failedQueues.Add(queue);
                continue;
            }

            double percentDiff = (waitTime.TotalSeconds - movingAvgWaitTime.Value.TotalSeconds) /
                                 movingAvgWaitTime.Value.TotalSeconds;

            highWaitTimeQueues.Add(new HighestWaitTimeQueueInfoView(queue, waitTime, percentDiff));
        }

        return new QueueInsightsResult<ImmutableList<HighestWaitTimeQueueInfoView>>(
            highWaitTimeQueues.ToImmutableList(),
            failedQueues.ToImmutableList());
    }
}
