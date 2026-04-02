// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Common.Cache;
using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet.DarcLib;
using ProductConstructionService.DependencyFlow.Model;
using ProductConstructionService.DependencyFlow.PullRequestUpdaters;
using ProductConstructionService.DependencyFlow.WorkItems;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.DependencyFlow;

/// <summary>
///     Manages the Redis-backed state for a pull request updater.
/// </summary>
internal class PullRequestStateManager : IPullRequestStateManager
{
    private readonly IPullRequestTarget _pullRequestTarget;
    private readonly IRedisCache<InProgressPullRequest> _pullRequestState;
    private readonly IRedisCache<MergePolicyEvaluationResults> _mergePolicyEvaluationState;
    private readonly IReminderManager<SubscriptionUpdateWorkItem> _pullRequestUpdateReminders;
    private readonly IReminderManager<PullRequestCheck> _pullRequestCheckReminders;

    public PullRequestStateManager(
        IPullRequestTarget pullRequestTarget,
        IRedisCacheFactory cacheFactory,
        IReminderManagerFactory reminderManagerFactory)
    {
        _pullRequestTarget = pullRequestTarget;
        var cacheKey = _pullRequestTarget.UpdaterId;
        _pullRequestState = cacheFactory.Create<InProgressPullRequest>(cacheKey);
        _mergePolicyEvaluationState = cacheFactory.Create<MergePolicyEvaluationResults>(cacheKey);
        _pullRequestUpdateReminders = reminderManagerFactory.CreateReminderManager<SubscriptionUpdateWorkItem>(cacheKey);
        _pullRequestCheckReminders = reminderManagerFactory.CreateReminderManager<PullRequestCheck>(cacheKey);
    }

    public Task<InProgressPullRequest?> GetInProgressPullRequestAsync() =>
        _pullRequestState.TryGetStateAsync();

    public Task SetInProgressPullRequestAsync(InProgressPullRequest pr) =>
        _pullRequestState.SetAsync(pr);

    public async Task UpdatePullRequestCreationDateAsync(InProgressPullRequest pr, DateTime creationDate)
    {
        // TODO (https://github.com/dotnet/arcade-services/issues/6146): Temporary solution to update existing PRs; can be removed after all existing PRs get a creation date
        if (pr.CreationDate != creationDate)
        {
            pr.CreationDate = creationDate;
            await _pullRequestState.SetAsync(pr);
        }
    }

    public Task<MergePolicyEvaluationResults?> GetMergePolicyEvaluationResultsAsync() =>
        _mergePolicyEvaluationState.TryGetStateAsync();

    public Task SetMergePolicyEvaluationResultsAsync(MergePolicyEvaluationResults results) =>
        _mergePolicyEvaluationState.SetAsync(results);

    public Task ClearMergePolicyEvaluationStateAsync() =>
        _mergePolicyEvaluationState.TryDeleteAsync();

    public async Task SetCheckReminderAsync(InProgressPullRequest prState, PullRequest prInfo, bool isCodeFlow, TimeSpan reminderDelay)
    {
        var reminder = new PullRequestCheck()
        {
            UpdaterId = _pullRequestTarget.UpdaterId,
            Url = prState.Url,
            IsCodeFlow = isCodeFlow
        };

        prState.LastCheck = DateTime.UtcNow;
        prState.NextCheck = prState.LastCheck + reminderDelay;
        prState.HeadBranchSha = prInfo.HeadBranchSha;

        await _pullRequestCheckReminders.SetReminderAsync(reminder, reminderDelay, isCodeFlow);
        await _pullRequestState.SetAsync(prState);
    }

    public Task SetCheckReminderAsync(InProgressPullRequest prState, PullRequest prInfo, bool isCodeFlow) =>
        SetCheckReminderAsync(prState, prInfo, isCodeFlow, PullRequestUpdater.DefaultReminderDelay);

    public Task UnsetCheckReminderAsync(bool isCodeFlow) =>
        _pullRequestCheckReminders.UnsetReminderAsync(isCodeFlow);

    public Task SetUpdateReminderAsync(SubscriptionUpdateWorkItem update, TimeSpan delay, bool isCodeFlow) =>
        _pullRequestUpdateReminders.SetReminderAsync(update, delay, isCodeFlow);

    public Task UnsetUpdateReminderAsync(bool isCodeFlow) =>
        _pullRequestUpdateReminders.UnsetReminderAsync(isCodeFlow);

    public async Task ClearAllStateAsync(bool isCodeFlow, bool clearPendingUpdates)
    {
        await _pullRequestState.TryDeleteAsync();
        await _pullRequestCheckReminders.UnsetReminderAsync(isCodeFlow);
        if (clearPendingUpdates)
        {
            await _pullRequestUpdateReminders.UnsetReminderAsync(isCodeFlow); 
        }
    }

    public async Task ScheduleUpdateForLater(InProgressPullRequest pr, SubscriptionUpdateWorkItem update, bool isCodeFlow)
    {
        await SetUpdateReminderAsync(update, PullRequestUpdater.DefaultReminderDelay, isCodeFlow);
        await UnsetCheckReminderAsync(isCodeFlow);
        pr.NextBuildsToProcess[update.SubscriptionId] = update.BuildId;
        await SetInProgressPullRequestAsync(pr);
    }
}
