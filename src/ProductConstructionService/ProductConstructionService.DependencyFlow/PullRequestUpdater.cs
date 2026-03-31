// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Common.Cache;
using Maestro.Common.Telemetry;
using Maestro.Data;
using Maestro.Data.Models;
using Maestro.DataProviders;
using Maestro.MergePolicies;
using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.Logging;
using ProductConstructionService.DependencyFlow.Model;
using ProductConstructionService.DependencyFlow.WorkItems;
using ProductConstructionService.WorkItems;

using BuildDTO = Microsoft.DotNet.ProductConstructionService.Client.Models.Build;

namespace ProductConstructionService.DependencyFlow;

/// <summary>
///     A class responsible for creating and updating pull requests for dependency updates.
/// </summary>
internal abstract class PullRequestUpdater : IPullRequestUpdater
{
    private readonly IPullRequestCommenter _pullRequestCommenter;
    private readonly IPullRequestChecker _pullRequestChecker;
    private readonly ISubscriptionConfiguration _subscriptionConfiguration;
    private readonly ISqlBarClient _sqlClient;
    private readonly ILogger<PullRequestUpdater> _logger;

    protected readonly IReminderManager<SubscriptionUpdateWorkItem> _pullRequestUpdateReminders;
    protected readonly IReminderManager<PullRequestCheck> _pullRequestCheckReminders;
    protected readonly IRedisCache<InProgressPullRequest> _pullRequestState;

    public PullRequestUpdaterId Id => (PullRequestUpdaterId)_subscriptionConfiguration;

    public PullRequestUpdater(
        ISubscriptionConfiguration subscriptionConfiguration,
        IPullRequestChecker pullRequestChecker,
        IRedisCacheFactory cacheFactory,
        IReminderManagerFactory reminderManagerFactory,
        ISqlBarClient sqlClient,
        ILogger<PullRequestUpdater> logger,
        IPullRequestCommenter pullRequestCommenter)
    {
        _subscriptionConfiguration = subscriptionConfiguration;
        _pullRequestChecker = pullRequestChecker;
        _sqlClient = sqlClient;
        _logger = logger;

        var cacheKey = Id.ToString();
        _pullRequestUpdateReminders = reminderManagerFactory.CreateReminderManager<SubscriptionUpdateWorkItem>(cacheKey);
        _pullRequestCheckReminders = reminderManagerFactory.CreateReminderManager<PullRequestCheck>(cacheKey);
        _pullRequestState = cacheFactory.Create<InProgressPullRequest>(cacheKey);
        _pullRequestCommenter = pullRequestCommenter;
    }

    protected abstract Task ProcessSubscriptionUpdateAsync(
        SubscriptionUpdateWorkItem update,
        InProgressPullRequest? pr,
        PullRequest? prInfo,
        BuildDTO build,
        bool forceUpdate);

    /// <summary>
    ///     Applies or queues asset updates for the target repository and branch from the given build and list of assets.
    /// </summary>
    /// <param name="subscriptionId">The id of the subscription the update comes from</param>
    /// <param name="buildId">The build that the updated assets came from</param>
    /// <remarks>
    ///     This function will queue updates if there is a pull request and it is currently not-updateable.
    ///     A pull request is considered "not-updateable" based on merge policies.
    ///     If at least one merge policy calls <see cref="IMergePolicyEvaluationContext.Pending" /> and
    ///     no merge policy calls <see cref="IMergePolicyEvaluationContext.Fail" /> then the pull request is considered
    ///     not-updateable.
    ///
    ///     PRs are marked as non-updateable so that we can allow pull request checks to complete on a PR prior
    ///     to pushing additional commits.
    /// </remarks>
    public async Task UpdateAssetsAsync(
        Guid subscriptionId,
        SubscriptionType type,
        int buildId,
        bool applyNewestOnly,
        bool forceUpdate = false)
    {
        var build = await _sqlClient.GetBuildAsync(buildId)
            ?? throw new InvalidOperationException($"Build with buildId {buildId} not found in the DB.");

        await ProcessPendingUpdatesAsync(
            new()
            {
                UpdaterId = Id.ToString(),
                SubscriptionId = subscriptionId,
                SubscriptionType = type,
                BuildId = buildId,
                SourceSha = build.Commit,
                SourceRepo = build.GetRepository(),
                IsCoherencyUpdate = false,
            },
            applyNewestOnly,
            forceUpdate,
            build);
    }

    /// <summary>
    ///     Process any pending pull request updates.
    /// </summary>
    /// <param name="applyNewestOnly">If true, we will check if this build is the latest one we have queued. If it's not we will skip this update.</param>
    /// <param name="forceUpdate">If true, force update even for PRs with pending or successful checks.</param>
    public async Task ProcessPendingUpdatesAsync(SubscriptionUpdateWorkItem update, bool applyNewestOnly, bool forceUpdate, BuildDTO build)
    {
        _logger.LogInformation("Processing pending updates for subscription {subscriptionId} with build {buildId}", update.SubscriptionId, build.Id);
        bool isCodeFlow = update.SubscriptionType == SubscriptionType.DependenciesAndSources;
        InProgressPullRequest? pr = await _pullRequestState.TryGetStateAsync();
        PullRequest? prInfo;

        if (pr == null)
        {
            _logger.LogInformation("No existing pull request state found");
            prInfo = null;
        }
        else
        {
            if (applyNewestOnly &&
                pr.NextBuildsToProcess != null &&
                pr.NextBuildsToProcess.TryGetValue(update.SubscriptionId, out int buildId) &&
                buildId != update.BuildId)
            {
                _logger.LogInformation("Skipping update for subscription {subscriptionId} with build {oldBuild} because an update with a newer build {newBuild} has already been queued.",
                    update.SubscriptionId,
                    update.BuildId,
                    pr.NextBuildsToProcess);
                return;
            }

            (var status, prInfo) = await _pullRequestChecker.GetPullRequestStatusAsync(pr, isCodeFlow, tryingToUpdate: true);

            await _pullRequestChecker.UpdatePullRequestCreationDateAsync(pr, prInfo.CreationDate.UtcDateTime);

            switch (status)
            {
                case PullRequestStatus.Completed:
                case PullRequestStatus.Invalid:
                    // If the PR is completed, we will open a new one
                    pr = null;
                    break;
                case PullRequestStatus.InProgressCanUpdate:
                    // If we can update it, we will do it below
                    break;
                case PullRequestStatus.InProgressCannotUpdate:
                    if (forceUpdate)
                    {
                        _logger.LogInformation("PR {url} cannot be updated normally but forcing update due to --force flag", pr.Url);
                        // Continue with the update despite the status
                        break;
                    }
                    await ScheduleUpdateForLater(pr, update, isCodeFlow);
                    return;
                default:
                    throw new NotImplementedException($"Unknown PR status {status}");
            }
        }

        await ProcessSubscriptionUpdateAsync(update, pr, prInfo, build, forceUpdate);

        pr = await _pullRequestState.TryGetStateAsync();
        if (pr != null)
        {
            await _pullRequestCommenter.PostCollectedCommentsAsync(
                pr.Url,
                (await _subscriptionConfiguration.GetTargetAsync()).Repository,
                [("<subscriptionId>", update.SubscriptionId.ToString())]);
        }
    }

    /// <summary>
    /// Merges the list of existing updates in a PR with a list of incoming updates
    /// </summary>
    /// <param name="existingUpdates">pr object to update</param>
    /// <param name="incomingUpdates">list of new incoming updates</param>
    /// <returns>Merged list of existing updates along with the new</returns>
    protected static List<DependencyUpdateSummary> MergeExistingWithIncomingUpdates(
        List<DependencyUpdateSummary> existingUpdates,
        List<DependencyUpdateSummary> incomingUpdates)
    {
        IEnumerable<DependencyUpdateSummary> mergedUpdates = existingUpdates
            .Select(u =>
            {
                var matchingIncoming = incomingUpdates
                    .FirstOrDefault(i => i.DependencyName == u.DependencyName && i.RelativeBasePath == u.RelativeBasePath);
                return new DependencyUpdateSummary()
                {
                    DependencyName = u.DependencyName,
                    FromCommitSha = u.FromCommitSha,
                    FromVersion = u.FromVersion,
                    ToCommitSha = matchingIncoming != null ? matchingIncoming.ToCommitSha : u.ToCommitSha,
                    ToVersion = matchingIncoming != null ? matchingIncoming.ToVersion : u.ToVersion,
                    RelativeBasePath = u.RelativeBasePath,
                };
            });

        IEnumerable<DependencyUpdateSummary> newUpdates = incomingUpdates
            .Where(u => !existingUpdates.Any(e => u.DependencyName == e.DependencyName && u.RelativeBasePath == e.RelativeBasePath));

        return [.. mergedUpdates, .. newUpdates];
    }

    protected static string GetNewBranchName(string targetBranch) => $"darc-{targetBranch}-{Guid.NewGuid()}";

    protected Task SetPullRequestCheckReminder(InProgressPullRequest prState, PullRequest prInfo, bool isCodeFlow, TimeSpan reminderDelay)
        => _pullRequestChecker.SetPullRequestCheckReminder(prState, prInfo, isCodeFlow, reminderDelay);

    protected Task SetPullRequestCheckReminder(InProgressPullRequest prState, PullRequest prInfo, bool isCodeFlow)
        => _pullRequestChecker.SetPullRequestCheckReminder(prState, prInfo, isCodeFlow);

    protected Task ClearAllStateAsync(bool isCodeFlow, bool clearPendingUpdates)
        => _pullRequestChecker.ClearAllStateAsync(isCodeFlow, clearPendingUpdates);

    protected Task AddDependencyFlowEventsAsync(
        IEnumerable<SubscriptionPullRequestUpdate> subscriptionPullRequestUpdates,
        DependencyFlowEventType flowEvent,
        DependencyFlowEventReason reason,
        MergePolicyCheckResult policy,
        string? prUrl)
        => _pullRequestChecker.AddDependencyFlowEventsAsync(subscriptionPullRequestUpdates, flowEvent, reason, policy, prUrl);

    protected Task RegisterSubscriptionUpdateAction(
        SubscriptionUpdateAction subscriptionUpdateAction,
        Guid subscriptionId)
        => _pullRequestChecker.RegisterSubscriptionUpdateAction(subscriptionUpdateAction, subscriptionId);

    protected Task UpdateSubscriptionsForMergedPRAsync(IEnumerable<SubscriptionPullRequestUpdate> subscriptionPullRequestUpdates)
        => _pullRequestChecker.UpdateSubscriptionsForMergedPRAsync(subscriptionPullRequestUpdates);

    protected Task<(IReadOnlyList<MergePolicyDefinition> policyDefinitions, MergePolicyEvaluationResults updatedResult)> RunMergePolicyEvaluation(
        InProgressPullRequest pr,
        PullRequest prInfo,
        IRemote remote)
        => _pullRequestChecker.RunMergePolicyEvaluation(pr, prInfo, remote);

    protected Task ClearMergePolicyEvaluationStateAsync()
        => _pullRequestChecker.ClearMergePolicyEvaluationStateAsync();

    private async Task ScheduleUpdateForLater(InProgressPullRequest pr, SubscriptionUpdateWorkItem update, bool isCodeFlow)
    {
        _logger.LogInformation("PR {url} for subscription {subscriptionId} cannot be updated at this time. Deferring update..", pr.Url, update.SubscriptionId);
        await _pullRequestUpdateReminders.SetReminderAsync(update, DependencyFlowConstants.DefaultReminderDelay, isCodeFlow);
        await _pullRequestCheckReminders.UnsetReminderAsync(isCodeFlow);
        pr.NextBuildsToProcess[update.SubscriptionId] = update.BuildId;
        await _pullRequestState.SetAsync(pr);
    }
}
