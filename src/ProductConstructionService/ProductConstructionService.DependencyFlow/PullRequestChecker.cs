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

namespace ProductConstructionService.DependencyFlow;

/// <summary>
///     Handles PR status checking, merge policy evaluation, and PR lifecycle management.
/// </summary>
internal class PullRequestChecker : IPullRequestChecker
{
    private readonly PullRequestUpdaterId _id;
    private readonly ISubscriptionConfiguration _subscriptionConfiguration;
    private readonly IMergePolicyEvaluator _mergePolicyEvaluator;
    private readonly BuildAssetRegistryContext _context;
    private readonly IRemoteFactory _remoteFactory;
    private readonly IPullRequestUpdaterFactory _updaterFactory;
    private readonly ISqlBarClient _sqlClient;
    private readonly ILogger<PullRequestChecker> _logger;

    private readonly IRedisCache<InProgressPullRequest> _pullRequestState;
    private readonly IRedisCache<MergePolicyEvaluationResults> _mergePolicyEvaluationState;
    private readonly IReminderManager<SubscriptionUpdateWorkItem> _pullRequestUpdateReminders;
    private readonly IReminderManager<PullRequestCheck> _pullRequestCheckReminders;

    public PullRequestChecker(
        ISubscriptionConfiguration subscriptionConfiguration,
        IMergePolicyEvaluator mergePolicyEvaluator,
        BuildAssetRegistryContext context,
        IRemoteFactory remoteFactory,
        IPullRequestUpdaterFactory updaterFactory,
        IRedisCacheFactory cacheFactory,
        IReminderManagerFactory reminderManagerFactory,
        ISqlBarClient sqlClient,
        ILogger<PullRequestChecker> logger)
    {
        _id = (PullRequestUpdaterId)subscriptionConfiguration;
        _subscriptionConfiguration = subscriptionConfiguration;
        _mergePolicyEvaluator = mergePolicyEvaluator;
        _context = context;
        _remoteFactory = remoteFactory;
        _updaterFactory = updaterFactory;
        _sqlClient = sqlClient;
        _logger = logger;

        var cacheKey = _id.ToString();
        _pullRequestUpdateReminders = reminderManagerFactory.CreateReminderManager<SubscriptionUpdateWorkItem>(cacheKey);
        _pullRequestCheckReminders = reminderManagerFactory.CreateReminderManager<PullRequestCheck>(cacheKey);
        _pullRequestState = cacheFactory.Create<InProgressPullRequest>(cacheKey);
        _mergePolicyEvaluationState = cacheFactory.Create<MergePolicyEvaluationResults>(cacheKey);
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

        if (!await _subscriptionConfiguration.IsAvailableAsync())
        {
            await ClearAllStateAsync(isCodeFlow: true, clearPendingUpdates: true);
            await ClearAllStateAsync(isCodeFlow: false, clearPendingUpdates: true);
            // Return true for test PRs to avoid reporting failure for deleted subscriptions during E2E tests
            return inProgressPr.Url?.Contains("maestro-auth-test") ?? false;
        }

        return await CheckInProgressPullRequestAsync(inProgressPr, pullRequestCheck.IsCodeFlow);
    }

    public virtual async Task<bool> CheckInProgressPullRequestAsync(InProgressPullRequest pr, bool isCodeFlow)
    {
        _logger.LogInformation("Checking in-progress pull request {url}", pr.Url);
        (var status, var prInfo) = await GetPullRequestStatusAsync(pr, isCodeFlow, tryingToUpdate: false);
        await UpdatePullRequestCreationDateAsync(pr, prInfo.CreationDate.UtcDateTime);
        return status != PullRequestStatus.Invalid;
    }

    public async Task UpdatePullRequestCreationDateAsync(InProgressPullRequest pr, DateTime creationDate)
    {
        //todo this is a temporary solution to update existing PRs, it can be removed after all existing PRs get a creation date
        if (pr.CreationDate == creationDate)
        {
            pr.CreationDate = creationDate;
            await _pullRequestState.SetAsync(pr);
        }
    }

    public async Task<(PullRequestStatus Status, PullRequest PrInfo)> GetPullRequestStatusAsync(InProgressPullRequest pr, bool isCodeFlow, bool tryingToUpdate)
    {
        _logger.LogInformation("Querying status for pull request {prUrl}", pr.Url);

        (var targetRepository, _) = await _subscriptionConfiguration.GetTargetAsync();
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
            ? DependencyFlowConstants.DefaultReminderDelay
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
                        await _subscriptionConfiguration.TagSourceRepositoryGitHubContactsIfPossibleAsync(pr);
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

    public async Task<(IReadOnlyList<MergePolicyDefinition> policyDefinitions, MergePolicyEvaluationResults updatedResult)> RunMergePolicyEvaluation(
        InProgressPullRequest pr,
        PullRequest prInfo,
        IRemote remote)
    {
        (var targetRepository, _) = await _subscriptionConfiguration.GetTargetAsync();
        IReadOnlyList<MergePolicyDefinition> policyDefinitions = await _subscriptionConfiguration.GetMergePolicyDefinitionsAsync();
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
    private static Task UpdateMergeStatusAsync(IRemote remote, string prUrl, IReadOnlyCollection<MergePolicyEvaluationResult> evaluations)
    {
        return remote.CreateOrUpdatePullRequestMergeStatusInfoAsync(prUrl, evaluations);
    }

    public async Task UpdateSubscriptionsForMergedPRAsync(IEnumerable<SubscriptionPullRequestUpdate> subscriptionPullRequestUpdates)
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

    public async Task AddDependencyFlowEventsAsync(
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

    public async Task SetPullRequestCheckReminder(InProgressPullRequest prState, PullRequest prInfo, bool isCodeFlow, TimeSpan reminderDelay)
    {
        var reminder = new PullRequestCheck()
        {
            UpdaterId = _id.ToString(),
            Url = prState.Url,
            IsCodeFlow = isCodeFlow
        };

        prState.LastCheck = DateTime.UtcNow;
        prState.NextCheck = prState.LastCheck + reminderDelay;
        prState.HeadBranchSha = prInfo.HeadBranchSha;

        await _pullRequestCheckReminders.SetReminderAsync(reminder, reminderDelay, isCodeFlow);
        await _pullRequestState.SetAsync(prState);
    }

    public async Task SetPullRequestCheckReminder(InProgressPullRequest prState, PullRequest prInfo, bool isCodeFlow) =>
        await SetPullRequestCheckReminder(prState, prInfo, isCodeFlow, DependencyFlowConstants.DefaultReminderDelay);

    public async Task ClearAllStateAsync(bool isCodeFlow, bool clearPendingUpdates)
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

    public async Task RegisterSubscriptionUpdateAction(
        SubscriptionUpdateAction subscriptionUpdateAction,
        Guid subscriptionId)
    {
        string updateMessage = subscriptionUpdateAction.ToString();
        await _sqlClient.RegisterSubscriptionUpdate(subscriptionId, updateMessage);
    }

    public async Task ClearMergePolicyEvaluationStateAsync()
    {
        await _mergePolicyEvaluationState.TryDeleteAsync();
    }

    internal static TimeSpan GetReminderDelay(DateTimeOffset updatedAt)
    {
        TimeSpan difference = DateTimeOffset.UtcNow - updatedAt;
        return difference.TotalDays switch
        {
            >= 30 => TimeSpan.FromHours(12),
            >= 21 => TimeSpan.FromHours(1),
            >= 14 => TimeSpan.FromMinutes(30),
            >= 2 => TimeSpan.FromMinutes(15),
            _ => DependencyFlowConstants.DefaultReminderDelay,
        };
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
