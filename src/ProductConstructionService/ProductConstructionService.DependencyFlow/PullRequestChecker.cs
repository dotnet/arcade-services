// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using Maestro.Data.Models;
using Maestro.MergePolicies;
using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.Extensions.Logging;
using ProductConstructionService.DependencyFlow.Model;
using ProductConstructionService.DependencyFlow.WorkItems;

namespace ProductConstructionService.DependencyFlow;

/// <summary>
///     Handles PR status checking, merge policy evaluation, and PR lifecycle management.
/// </summary>
internal class PullRequestChecker : IPullRequestChecker
{
    private readonly IPullRequestTarget _target;
    private readonly IPullRequestStateManager _stateManager;
    private readonly IMergePolicyEvaluator _mergePolicyEvaluator;
    private readonly IRemoteFactory _remoteFactory;
    private readonly ISubscriptionEventRecorder _subscriptionEventRecorder;
    private readonly ILogger<PullRequestChecker> _logger;

    public PullRequestChecker(
        IPullRequestTarget target,
        IPullRequestStateManager stateManager,
        IMergePolicyEvaluator mergePolicyEvaluator,
        IRemoteFactory remoteFactory,
        ISubscriptionEventRecorder subscriptionEventRecorder,
        ILogger<PullRequestChecker> logger)
    {
        _target = target;
        _stateManager = stateManager;
        _mergePolicyEvaluator = mergePolicyEvaluator;
        _remoteFactory = remoteFactory;
        _subscriptionEventRecorder = subscriptionEventRecorder;
        _logger = logger;
    }

    public async Task<bool> CheckPullRequestAsync(PullRequestCheck pullRequestCheck)
    {
        var inProgressPr = await _stateManager.GetInProgressPullRequestAsync();

        if (inProgressPr == null)
        {
            _logger.LogInformation("No in-progress pull request found for a PR check");
            await _stateManager.ClearAllStateAsync(isCodeFlow: true, clearPendingUpdates: false);
            await _stateManager.ClearAllStateAsync(isCodeFlow: false, clearPendingUpdates: false);
            return false;
        }

        if (!await _target.ShouldContinueProcessingAsync())
        {
            await _stateManager.ClearAllStateAsync(isCodeFlow: true, clearPendingUpdates: true);
            await _stateManager.ClearAllStateAsync(isCodeFlow: false, clearPendingUpdates: true);
            // Return true for test PRs to avoid reporting failure for deleted subscriptions during E2E tests
            return inProgressPr.Url?.Contains("maestro-auth-test") ?? false;
        }

        return await CheckInProgressPullRequestAsync(inProgressPr, pullRequestCheck.IsCodeFlow);
    }

    public virtual async Task<bool> CheckInProgressPullRequestAsync(InProgressPullRequest pr, bool isCodeFlow)
    {
        _logger.LogInformation("Checking in-progress pull request {url}", pr.Url);
        (var status, var prInfo) = await GetPullRequestStatusAsync(pr, isCodeFlow, tryingToUpdate: false);
        await _stateManager.UpdatePullRequestCreationDateAsync(pr, prInfo.CreationDate.UtcDateTime);
        return status != PullRequestStatus.Invalid;
    }

    public async Task<(PullRequestStatus Status, PullRequest PrInfo)> GetPullRequestStatusAsync(InProgressPullRequest pr, bool isCodeFlow, bool tryingToUpdate)
    {
        _logger.LogInformation("Querying status for pull request {prUrl}", pr.Url);

        (var targetRepository, _) = await _target.GetTargetAsync();
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
                        await _subscriptionEventRecorder.UpdateSubscriptionsForMergedPRAsync(pr.ContainedSubscriptions);
                        await _subscriptionEventRecorder.AddDependencyFlowEventsAsync(
                            pr.ContainedSubscriptions,
                            DependencyFlowEventType.Completed,
                            DependencyFlowEventReason.AutomaticallyMerged,
                            mergePolicyResult,
                            pr.Url);

                        // If the PR we just merged was in conflict with an update we previously tried to apply, we shouldn't delete the reminder for the update
                        await _stateManager.ClearAllStateAsync(isCodeFlow, false);
                        return (PullRequestStatus.Completed, prInfo);

                    case MergePolicyCheckResult.FailedPolicies:
                        await _target.TagSourceRepositoryGitHubContactsIfPossibleAsync(pr);
                        goto case MergePolicyCheckResult.FailedToMerge;

                    case MergePolicyCheckResult.NoPolicies:
                    case MergePolicyCheckResult.FailedToMerge:
                        _logger.LogInformation("Pull request {url} can be updated", pr.Url);
                        await _stateManager.SetCheckReminderAsync(pr, prInfo, isCodeFlow, delay);

                        return (PullRequestStatus.InProgressCanUpdate, prInfo);

                    case MergePolicyCheckResult.PendingPolicies:
                        _logger.LogInformation("Pull request {url} still active (not updatable at the moment) - keeping tracking it", pr.Url);
                        await _stateManager.SetCheckReminderAsync(pr, prInfo, isCodeFlow, delay);

                        return (PullRequestStatus.InProgressCannotUpdate, prInfo);

                    default:
                        await _stateManager.SetCheckReminderAsync(pr, prInfo, isCodeFlow, delay);
                        throw new NotImplementedException($"Unknown merge policy check result {mergePolicyResult}");
                }

            case PrStatus.Merged:
            case PrStatus.Closed:
                // If the PR has been merged, update the subscription information
                if (prInfo.Status == PrStatus.Merged)
                {
                    await _subscriptionEventRecorder.UpdateSubscriptionsForMergedPRAsync(pr.ContainedSubscriptions);
                }

                DependencyFlowEventReason reason = prInfo.Status == PrStatus.Merged
                    ? DependencyFlowEventReason.ManuallyMerged
                    : DependencyFlowEventReason.ManuallyClosed;

                await _subscriptionEventRecorder.AddDependencyFlowEventsAsync(
                    pr.ContainedSubscriptions,
                    DependencyFlowEventType.Completed,
                    reason,
                    pr.MergePolicyResult,
                    pr.Url);

                _logger.LogInformation("PR {url} has been manually {action}. Stopping tracking it", pr.Url, prInfo.Status.ToString().ToLowerInvariant());

                await _stateManager.ClearAllStateAsync(isCodeFlow, clearPendingUpdates: false);

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
                await _subscriptionEventRecorder.RegisterSubscriptionUpdateAction(SubscriptionUpdateAction.MergingPullRequest, subscription.SubscriptionId);
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
        (var targetRepository, _) = await _target.GetTargetAsync();
        IReadOnlyList<MergePolicyDefinition> policyDefinitions = await _target.GetMergePolicyDefinitionsAsync();
        PullRequestUpdateSummary prSummary = CreatePrSummaryFromInProgressPr(pr, targetRepository);
        MergePolicyEvaluationResults? cachedResults = await _stateManager.GetMergePolicyEvaluationResultsAsync();

        IEnumerable<MergePolicyEvaluationResult> updatedMergePolicyResults = await _mergePolicyEvaluator.EvaluateAsync(
            prSummary,
            remote,
            policyDefinitions,
            cachedResults,
            prInfo.HeadBranchSha);

        MergePolicyEvaluationResults updatedResult = new(
            updatedMergePolicyResults.ToImmutableList(),
            prInfo.HeadBranchSha);

        await _stateManager.SetMergePolicyEvaluationResultsAsync(updatedResult);

        await UpdateMergeStatusAsync(remote, pr.Url, updatedResult.Results);
        return (policyDefinitions, updatedResult);
    }

    /// <summary>
    ///     Create new checks or update the status of existing checks for a PR.
    /// </summary>
    private static Task UpdateMergeStatusAsync(IRemote remote, string prUrl, IReadOnlyCollection<MergePolicyEvaluationResult> evaluations) =>
        remote.CreateOrUpdatePullRequestMergeStatusInfoAsync(prUrl, evaluations);

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
        string targetRepo) =>
        new(
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
