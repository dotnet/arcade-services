// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet.DarcLib;
using ProductConstructionService.DependencyFlow.Model;
using ProductConstructionService.DependencyFlow.WorkItems;

namespace ProductConstructionService.DependencyFlow;

/// <summary>
///     Manages the Redis-backed state for a pull request updater:
///     in-progress PR state, merge policy evaluation cache, check reminders, and update reminders.
/// </summary>
internal interface IPullRequestStateManager
{
    #region In-progress PR state

    Task<InProgressPullRequest?> GetInProgressPullRequestAsync();

    Task SetInProgressPullRequestAsync(InProgressPullRequest pr);

    /// <summary>
    ///     Updates the creation date on the in-progress PR if it differs.
    ///     Temporary workaround until all existing PRs have a creation date.
    /// </summary>
    Task UpdatePullRequestCreationDateAsync(InProgressPullRequest pr, DateTime creationDate);

    #endregion

    #region Merge policy evaluation cache

    Task<MergePolicyEvaluationResults?> GetMergePolicyEvaluationResultsAsync();

    Task SetMergePolicyEvaluationResultsAsync(MergePolicyEvaluationResults results);

    Task ClearMergePolicyEvaluationStateAsync();

    #endregion

    #region Check reminders

    Task SetCheckReminderAsync(InProgressPullRequest prState, PullRequest prInfo, bool isCodeFlow, TimeSpan reminderDelay);

    Task SetCheckReminderAsync(InProgressPullRequest prState, PullRequest prInfo, bool isCodeFlow);

    Task UnsetCheckReminderAsync(bool isCodeFlow);

    #endregion

    #region Update reminders

    Task SetUpdateReminderAsync(SubscriptionUpdateWorkItem update, TimeSpan delay, bool isCodeFlow);

    Task UnsetUpdateReminderAsync(bool isCodeFlow);

    Task ScheduleUpdateForLater(InProgressPullRequest pr, SubscriptionUpdateWorkItem update, bool isCodeFlow);

    #endregion

    #region Bulk cleanup

    /// <summary>
    ///     Clears the in-progress PR state, check reminder and queued update
    /// </summary>
    Task ClearAllStateAsync(bool isCodeFlow, bool clearPendingUpdates);

    #endregion
}
