// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using Maestro.Common.Cache;
using Maestro.Common.Telemetry;
using Maestro.Data;
using Maestro.Data.Models;
using Maestro.DataProviders;
using Maestro.MergePolicies;
using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Models;
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
#if DEBUG
    private static readonly TimeSpan DefaultReminderDelay = TimeSpan.FromMinutes(1);
#else
    private static readonly TimeSpan DefaultReminderDelay = TimeSpan.FromMinutes(5);
#endif

    private readonly IPullRequestUpdaterFactory _updaterFactory;
    private readonly IMergePolicyEvaluator _mergePolicyEvaluator;
    private readonly IPullRequestCommenter _pullRequestCommenter;

    protected readonly BuildAssetRegistryContext _context;
    protected readonly IRemoteFactory _remoteFactory;
    protected readonly ICommentCollector _commentCollector;
    protected readonly ISqlBarClient _sqlClient;
    protected readonly ITelemetryRecorder _telemetryRecorder;
    protected readonly ILogger<PullRequestUpdater> _logger;

    protected readonly IReminderManager<SubscriptionUpdateWorkItem> _pullRequestUpdateReminders;
    protected readonly IReminderManager<PullRequestCheck> _pullRequestCheckReminders;
    protected readonly IRedisCache<InProgressPullRequest> _pullRequestState;
    protected readonly IRedisCache<MergePolicyEvaluationResults> _mergePolicyEvaluationState;

    public PullRequestUpdaterId Id { get; }

    public PullRequestUpdater(
        PullRequestUpdaterId id,
        IMergePolicyEvaluator mergePolicyEvaluator,
        BuildAssetRegistryContext context,
        IRemoteFactory remoteFactory,
        IPullRequestUpdaterFactory updaterFactory,
        IRedisCacheFactory cacheFactory,
        IReminderManagerFactory reminderManagerFactory,
        ISqlBarClient sqlClient,
        ITelemetryRecorder telemetryRecorder,
        ILogger<PullRequestUpdater> logger,
        ICommentCollector commentCollector,
        IPullRequestCommenter pullRequestCommenter)
    {
        Id = id;
        _mergePolicyEvaluator = mergePolicyEvaluator;
        _context = context;
        _remoteFactory = remoteFactory;
        _updaterFactory = updaterFactory;
        _sqlClient = sqlClient;
        _telemetryRecorder = telemetryRecorder;
        _logger = logger;

        var cacheKey = id.ToString();
        _pullRequestUpdateReminders = reminderManagerFactory.CreateReminderManager<SubscriptionUpdateWorkItem>(cacheKey);
        _pullRequestCheckReminders = reminderManagerFactory.CreateReminderManager<PullRequestCheck>(cacheKey);
        _pullRequestState = cacheFactory.Create<InProgressPullRequest>(cacheKey);
        _mergePolicyEvaluationState = cacheFactory.Create<MergePolicyEvaluationResults>(cacheKey);
        _commentCollector = commentCollector;
        _pullRequestCommenter = pullRequestCommenter;
    }

    protected abstract Task<(string repository, string branch)> GetTargetAsync();

    protected abstract Task<IReadOnlyList<MergePolicyDefinition>> GetMergePolicyDefinitions();

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

            (var status, prInfo) = await GetPullRequestStatusAsync(pr, isCodeFlow, tryingToUpdate: true);

            await UpdatePullRequestCreationDateAsync(pr, prInfo.CreationDate.UtcDateTime);

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
                (await GetTargetAsync()).repository,
                [("<subscriptionId>", update.SubscriptionId.ToString())]);
        }
    }

    public async Task<bool> CheckPullRequestAsync(PullRequestCheck pullRequestCheck)
    {
        var inProgressPr = await _pullRequestState.TryGetStateAsync();

        if (inProgressPr == null)
        {
            _logger.LogInformation("No in-progress pull request found for a PR check");
            await ClearAllStateAsync(isCodeFlow: true, clearPendingUpdates: true);
            await ClearAllStateAsync(isCodeFlow: false, clearPendingUpdates: true);
            return false;
        }

        return await CheckInProgressPullRequestAsync(inProgressPr, pullRequestCheck.IsCodeFlow);
    }

    protected virtual async Task<bool> CheckInProgressPullRequestAsync(InProgressPullRequest pr, bool isCodeFlow)
    {
        _logger.LogInformation("Checking in-progress pull request {url}", pr.Url);
        (var status, var prInfo) = await GetPullRequestStatusAsync(pr, isCodeFlow, tryingToUpdate: false);
        await UpdatePullRequestCreationDateAsync(pr, prInfo.CreationDate.UtcDateTime);
        return status != PullRequestStatus.Invalid;
    }

    // TODO (https://github.com/dotnet/arcade-services/issues/6146) todo this is a temporary solution to update existing PRs
    private async Task UpdatePullRequestCreationDateAsync(InProgressPullRequest pr, DateTime creationDate)
    {
        if (pr.CreationDate != creationDate)
        {
            pr.CreationDate = creationDate;
            await _pullRequestState.SetAsync(pr);
        }
    }

    protected abstract Task TagSourceRepositoryGitHubContactsIfPossibleAsync(InProgressPullRequest pr);

    private async Task<(PullRequestStatus Status, PullRequest PrInfo)> GetPullRequestStatusAsync(InProgressPullRequest pr, bool isCodeFlow, bool tryingToUpdate)
    {
        _logger.LogInformation("Querying status for pull request {prUrl}", pr.Url);

        (var targetRepository, _) = await GetTargetAsync();
        var remote = await _remoteFactory.CreateRemoteAsync(targetRepository);

        PullRequest prInfo;
        try
        {
            prInfo = await remote.GetPullRequestAsync(pr.Url);
        }
        catch
        {
            _logger.LogError("Couldn't get status of PR {prUrl}", pr.Url);
            throw;
        }

        _logger.LogInformation("Pull request {url} is {status}", pr.Url, prInfo.Status);

        // If we're about to update the PR, we should set the default reminder delay,
        // otherwise we should use the time since the last update to determine when to check again
        var delay = tryingToUpdate
            ? DefaultReminderDelay
            : GetReminderDelay(prInfo.UpdatedAt);

        switch (prInfo.Status)
        {
            // If the PR is currently open, then evaluate the merge policies, which will potentially
            // merge the PR if they are successful.
            case PrStatus.Open:
                MergePolicyCheckResult mergePolicyResult = await TryMergingPrAsync(pr, prInfo, remote);

                _logger.LogInformation("Policy check status for pull request {url} is {result}", pr.Url, mergePolicyResult);

                switch (mergePolicyResult)
                {
                    // Policies evaluated successfully and the PR was merged just now
                    case MergePolicyCheckResult.Merged:
                        await UpdateSubscriptionsForMergedPRAsync(pr.ContainedSubscriptions);
                        await AddDependencyFlowEventsAsync(
                            pr.ContainedSubscriptions,
                            DependencyFlowEventType.Completed,
                            DependencyFlowEventReason.AutomaticallyMerged,
                            mergePolicyResult,
                            pr.Url);

                        // If the PR we just merged was in conflict with an update we previously tried to apply, we shouldn't delete the reminder for the update
                        await ClearAllStateAsync(isCodeFlow, clearPendingUpdates: true);
                        return (PullRequestStatus.Completed, prInfo);

                    case MergePolicyCheckResult.FailedPolicies:
                        await TagSourceRepositoryGitHubContactsIfPossibleAsync(pr);
                        goto case MergePolicyCheckResult.FailedToMerge;

                    case MergePolicyCheckResult.NoPolicies:
                    case MergePolicyCheckResult.FailedToMerge:
                        _logger.LogInformation("Pull request {url} can be updated", pr.Url);
                        await SetPullRequestCheckReminder(pr, prInfo, isCodeFlow, delay);

                        return (PullRequestStatus.InProgressCanUpdate, prInfo);

                    case MergePolicyCheckResult.PendingPolicies:
                        _logger.LogInformation("Pull request {url} still active (not updatable at the moment) - keeping tracking it", pr.Url);
                        await SetPullRequestCheckReminder(pr, prInfo, isCodeFlow, delay);

                        return (PullRequestStatus.InProgressCannotUpdate, prInfo);

                    default:
                        await SetPullRequestCheckReminder(pr, prInfo, isCodeFlow, delay);
                        throw new NotImplementedException($"Unknown merge policy check result {mergePolicyResult}");
                }

            case PrStatus.Merged:
            case PrStatus.Closed:
                // If the PR has been merged, update the subscription information
                if (prInfo.Status == PrStatus.Merged)
                {
                    await UpdateSubscriptionsForMergedPRAsync(pr.ContainedSubscriptions);
                }

                DependencyFlowEventReason reason = prInfo.Status == PrStatus.Merged
                    ? DependencyFlowEventReason.ManuallyMerged
                    : DependencyFlowEventReason.ManuallyClosed;

                await AddDependencyFlowEventsAsync(
                    pr.ContainedSubscriptions,
                    DependencyFlowEventType.Completed,
                    reason,
                    pr.MergePolicyResult,
                    pr.Url);

                _logger.LogInformation("PR {url} has been manually {action}. Stopping tracking it", pr.Url, prInfo.Status.ToString().ToLowerInvariant());

                await ClearAllStateAsync(isCodeFlow, clearPendingUpdates: true);

                // Also try to clean up the PR branch.
                try
                {
                    _logger.LogInformation("Trying to clean up the branch for pull request {url}", pr.Url);
                    await remote.DeletePullRequestBranchAsync(pr.Url);
                }
                catch (DarcException)
                {
                    _logger.LogInformation("Failed to delete branch associated with pull request {url}", pr.Url);
                }

                return (PullRequestStatus.Completed, prInfo);

            default:
                throw new NotImplementedException($"Unknown PR status '{prInfo.Status}'");
        }
    }

    /// <summary>
    ///     Check the merge policies for a PR and merge if they have succeeded.
    /// </summary>
    /// <param name="pr">Pull request</param>
    /// <param name="remote">Darc remote</param>
    /// <returns>Result of the policy check.</returns>
    private async Task<MergePolicyCheckResult> TryMergingPrAsync(
        InProgressPullRequest pr,
        PullRequest prInfo,
        IRemote remote)
    {
        (IReadOnlyList<MergePolicyDefinition> policyDefinitions, MergePolicyEvaluationResults updatedResult) = await RunMergePolicyEvaluation(pr, prInfo, remote);

        // As soon as one policy is actively failed, we enter a failed state.
        if (updatedResult.Failed)
        {
            _logger.LogInformation("NOT Merged: PR '{url}' failed policies {policies}",
                pr.Url,
                string.Join(Environment.NewLine, updatedResult.Results
                    .Where(r => r.Status is not MergePolicyEvaluationStatus.DecisiveSuccess && r.Status is not MergePolicyEvaluationStatus.TransientSuccess)
                    .Select(r => $"{r.MergePolicyName} - {r.Title}: " + r.Message)));

            return MergePolicyCheckResult.FailedPolicies;
        }

        if (updatedResult.Pending)
        {
            _logger.LogInformation("NOT Merged: PR '{url}' has pending policies {policies}",
                pr.Url,
                string.Join(Environment.NewLine, updatedResult.Results
                    .Where(r => r.Status == MergePolicyEvaluationStatus.Pending)
                    .Select(r => $"{r.MergePolicyName} - {r.Title}: " + r.Message)));
            return MergePolicyCheckResult.PendingPolicies;
        }

        if (!updatedResult.Succeeded)
        {
            _logger.LogInformation("NOT Merged: PR '{url}' There are no merge policies", pr.Url);
            return MergePolicyCheckResult.NoPolicies;
        }

        try
        {
            await remote.MergeDependencyPullRequestAsync(pr.Url, new MergePullRequestParameters());

            foreach (SubscriptionPullRequestUpdate subscription in pr.ContainedSubscriptions)
            {
                await RegisterSubscriptionUpdateAction(SubscriptionUpdateAction.MergingPullRequest, subscription.SubscriptionId);
            }

            var passedPolicies = string.Join(", ", policyDefinitions.Select(p => p.Name));
            _logger.LogInformation("Merged: PR '{url}' passed policies {passedPolicies}", pr.Url, passedPolicies);
            return MergePolicyCheckResult.Merged;
        }
        catch (PullRequestNotMergeableException notMergeableException)
        {
            _logger.LogInformation("NOT Merged: PR '{url}' is not mergeable - {message}", pr.Url, notMergeableException.Message);
            return MergePolicyCheckResult.FailedToMerge;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NOT Merged: Failed to merge PR '{url}' - {message}", pr.Url, ex.Message);
            return MergePolicyCheckResult.FailedToMerge;
        }
    }

    protected async Task<(IReadOnlyList<MergePolicyDefinition> policyDefinitions, MergePolicyEvaluationResults updatedResult)> RunMergePolicyEvaluation(
        InProgressPullRequest pr,
        PullRequest prInfo,
        IRemote remote)
    {
        (var targetRepository, _) = await GetTargetAsync();
        IReadOnlyList<MergePolicyDefinition> policyDefinitions = await GetMergePolicyDefinitions();
        PullRequestUpdateSummary prSummary = CreatePrSummaryFromInProgressPr(pr, targetRepository);
        MergePolicyEvaluationResults? cachedResults = await _mergePolicyEvaluationState.TryGetStateAsync();

        IEnumerable<MergePolicyEvaluationResult> updatedMergePolicyResults = await _mergePolicyEvaluator.EvaluateAsync(
            prSummary,
            remote,
            policyDefinitions,
            cachedResults,
            prInfo.HeadBranchSha);

        MergePolicyEvaluationResults updatedResult = new(
            updatedMergePolicyResults.ToImmutableList(),
            prInfo.HeadBranchSha);

        await _mergePolicyEvaluationState.SetAsync(updatedResult);

        await UpdateMergeStatusAsync(remote, pr.Url, updatedResult.Results);
        return (policyDefinitions, updatedResult);
    }

    /// <summary>
    ///     Create new checks or update the status of existing checks for a PR.
    /// </summary>
    /// <param name="prUrl">Pull request URL</param>
    /// <param name="remote">Darc remote</param>
    /// <param name="evaluations">List of merge policies</param>
    /// <returns>Result of the policy check.</returns>
    private static Task UpdateMergeStatusAsync(IRemote remote, string prUrl, IReadOnlyCollection<MergePolicyEvaluationResult> evaluations)
    {
        return remote.CreateOrUpdatePullRequestMergeStatusInfoAsync(prUrl, evaluations);
    }

    protected async Task UpdateSubscriptionsForMergedPRAsync(IEnumerable<SubscriptionPullRequestUpdate> subscriptionPullRequestUpdates)
    {
        _logger.LogInformation("Updating subscriptions for merged PR");
        foreach (SubscriptionPullRequestUpdate update in subscriptionPullRequestUpdates)
        {
            await UpdateForMergedPullRequestAsync(update);
        }
    }

    private async Task UpdateForMergedPullRequestAsync(SubscriptionPullRequestUpdate update)
    {
        _logger.LogInformation("Updating {subscriptionId} with latest build id {buildId}", update.SubscriptionId, update.BuildId);
        Subscription? subscription = await _context.Subscriptions.FindAsync(update.SubscriptionId);

        if (subscription != null)
        {
            subscription.LastAppliedBuildId = subscription.SourceEnabled
                // We must check if the build really got applied or if someone merged an earlier build without resolving conflicts
                ? await GetLastCodeflownBuild(subscription)
                : update.BuildId;
            _context.Subscriptions.Update(subscription);
            await _context.SaveChangesAsync();
        }
        else
        {
            // This happens for deleted subscriptions (such as scenario tests)
            _logger.LogInformation("Could not find subscription with ID {subscriptionId}. Skipping latestBuild update.", update.SubscriptionId);
        }
    }

    // TODO this name isn't good
    protected async Task AddDependencyFlowEventsAsync(
        IEnumerable<SubscriptionPullRequestUpdate> subscriptionPullRequestUpdates,
        DependencyFlowEventType flowEvent,
        DependencyFlowEventReason reason,
        MergePolicyCheckResult policy,
        string? prUrl)
    {
        foreach (SubscriptionPullRequestUpdate update in subscriptionPullRequestUpdates)
        {
            ISubscriptionTriggerer triggerer = _updaterFactory.CreateSubscriptionTrigerrer(update.SubscriptionId);
            if (!await triggerer.AddDependencyFlowEventAsync(update.BuildId, flowEvent, reason, policy, "PR", prUrl))
            {
                _logger.LogInformation("Failed to add dependency flow event for {subscriptionId}", update.SubscriptionId);
            }
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

    protected async Task SetPullRequestCheckReminder(InProgressPullRequest prState, PullRequest prInfo, bool isCodeFlow, TimeSpan reminderDelay)
    {
        var reminder = new PullRequestCheck()
        {
            UpdaterId = Id.ToString(),
            Url = prState.Url,
            IsCodeFlow = isCodeFlow
        };

        prState.LastCheck = DateTime.UtcNow;
        prState.NextCheck = prState.LastCheck + reminderDelay;
        prState.HeadBranchSha = prInfo.HeadBranchSha;

        await _pullRequestCheckReminders.SetReminderAsync(reminder, reminderDelay, isCodeFlow);
        await _pullRequestState.SetAsync(prState);
    }

    protected async Task SetPullRequestCheckReminder(InProgressPullRequest prSate, PullRequest prInfo, bool isCodeFlow) =>
        await SetPullRequestCheckReminder(prSate, prInfo, isCodeFlow, DefaultReminderDelay);

    protected async Task ClearAllStateAsync(bool isCodeFlow, bool clearPendingUpdates)
    {
        await _pullRequestState.TryDeleteAsync();
        await _pullRequestCheckReminders.UnsetReminderAsync(isCodeFlow);
        // If the pull request we deleted from the cache had a conflict, we shouldn't unset the update reminder
        // as there was an update that was previously blocked
        if (!clearPendingUpdates)
        {
            await _pullRequestUpdateReminders.UnsetReminderAsync(isCodeFlow);
        }
    }

    private async Task ScheduleUpdateForLater(InProgressPullRequest pr, SubscriptionUpdateWorkItem update, bool isCodeFlow)
    {
        _logger.LogInformation("PR {url} for subscription {subscriptionId} cannot be updated at this time. Deferring update..", pr.Url, update.SubscriptionId);
        await _pullRequestUpdateReminders.SetReminderAsync(update, DefaultReminderDelay, isCodeFlow);
        await _pullRequestCheckReminders.UnsetReminderAsync(isCodeFlow);
        pr.NextBuildsToProcess[update.SubscriptionId] = update.BuildId;
        await _pullRequestState.SetAsync(pr);
    }

    private static TimeSpan GetReminderDelay(DateTimeOffset updatedAt)
    {
        TimeSpan difference = DateTimeOffset.UtcNow - updatedAt;
        return difference.TotalDays switch
        {
            >= 30 => TimeSpan.FromHours(12),
            >= 21 => TimeSpan.FromHours(1),
            >= 14 => TimeSpan.FromMinutes(30),
            >= 2 => TimeSpan.FromMinutes(15),
            _ => DefaultReminderDelay,
        };
    }

    protected async Task RegisterSubscriptionUpdateAction(
        SubscriptionUpdateAction subscriptionUpdateAction,
        Guid subscriptionId)
    {
        string updateMessage = subscriptionUpdateAction.ToString();
        await _sqlClient.RegisterSubscriptionUpdate(subscriptionId, updateMessage);
    }

    private static PullRequestUpdateSummary CreatePrSummaryFromInProgressPr(
        InProgressPullRequest pr,
        string targetRepo)
    {
        return new PullRequestUpdateSummary(
            pr.Url,
            pr.CoherencyCheckSuccessful,
            pr.CoherencyErrors,
            pr.RequiredUpdates,
            pr.ContainedSubscriptions
                .Select(su => new SubscriptionUpdateSummary(
                    su.SubscriptionId,
                    su.BuildId,
                    su.SourceRepo,
                    su.CommitSha))
                .ToList(),
            pr.HeadBranch,
            targetRepo,
            pr.CodeFlowDirection);
    }

    private async Task<int> GetLastCodeflownBuild(Subscription subscription)
    {
        var remote = await _remoteFactory.CreateRemoteAsync(subscription.TargetRepository);
        if (!string.IsNullOrEmpty(subscription.SourceDirectory))
        {
            // Backflow
            var sourceTag = await remote.GetSourceDependencyAsync(subscription.TargetRepository, subscription.TargetBranch);

            return sourceTag?.BarId
                ?? throw new DarcException($"Failed to determine last flown VMR build " +
                                           $"to {subscription.TargetRepository} @ {subscription.TargetBranch}");
        }
        else
        {
            // Forward flow
            var sourceManifest = await remote.GetSourceManifestAsync(subscription.TargetRepository, subscription.TargetBranch);

            return sourceManifest.GetRepositoryRecord(subscription.TargetDirectory)?.BarId
                ?? throw new DarcException($"Failed to determine last flown build of {subscription.TargetDirectory} " +
                                           $"to {subscription.TargetRepository} @ {subscription.TargetBranch}");
        }
    }
}
