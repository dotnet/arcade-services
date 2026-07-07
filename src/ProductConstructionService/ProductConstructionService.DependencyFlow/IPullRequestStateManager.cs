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

    #region Codeflow approval check

    Task SetCodeflowApprovalCheck(CodeflowApprovalCheck check);

    #endregion

    #region Bulk cleanup

    /// <summary>
    ///     Clears the in-progress PR state, check reminder and queued update
    /// </summary>
    Task ClearAllStateAsync(bool isCodeFlow, bool clearPendingUpdates);

    #endregion
}
