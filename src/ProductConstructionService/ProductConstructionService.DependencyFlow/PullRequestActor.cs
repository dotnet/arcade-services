// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using Maestro.Contracts;
using Maestro.Data;
using Maestro.Data.Models;
using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.Logging;
using ProductConstructionService.DependencyFlow.StateModel;
using Asset = Maestro.Contracts.Asset;
using AssetData = Microsoft.DotNet.Maestro.Client.Models.AssetData;

namespace ProductConstructionService.DependencyFlow;

/// <summary>
///     A service fabric actor implementation that is responsible for creating and updating pull requests for dependency
///     updates.
/// </summary>
internal abstract class PullRequestActor : IPullRequestActor
{
    private readonly ActorId _id;
    private readonly IMergePolicyEvaluator _mergePolicyEvaluator;
    private readonly BuildAssetRegistryContext _context;
    private readonly IRemoteFactory _remoteFactory;
    private readonly IActorFactory _actorFactory;
    private readonly ICoherencyUpdateResolver _coherencyUpdateResolver;
    private readonly IPullRequestBuilder _pullRequestBuilder;
    private readonly IPullRequestPolicyFailureNotifier _pullRequestPolicyFailureNotifier;
    private readonly IReminderManager _reminderManager;
    private readonly ILogger _logger;

    protected readonly CollectionStateManager<UpdateAssetsParameters> _pullRequestUpdateState;
    protected readonly ReminderManager<UpdateAssetsParameters> _pullRequestCheckState;
    protected readonly StateManager<InProgressPullRequest> _pullRequestState;
    protected readonly StateManager<CodeFlowStatus> _codeFlowState;

    /// <summary>
    ///     Creates a new PullRequestActor
    /// </summary>
    public PullRequestActor(
        ActorId id,
        IReminderManager reminders,
        StateModel.IStateManager stateManager,
        IMergePolicyEvaluator mergePolicyEvaluator,
        BuildAssetRegistryContext context,
        IRemoteFactory remoteFactory,
        IActorFactory actorFactory,
        ICoherencyUpdateResolver coherencyUpdateResolver,
        IPullRequestBuilder pullRequestBuilder,
        IPullRequestPolicyFailureNotifier pullRequestPolicyFailureNotifier,
        IReminderManager reminderManager,
        ILogger logger)
    {
        _id = id;
        _mergePolicyEvaluator = mergePolicyEvaluator;
        _context = context;
        _remoteFactory = remoteFactory;
        _actorFactory = actorFactory;
        _coherencyUpdateResolver = coherencyUpdateResolver;
        _pullRequestBuilder = pullRequestBuilder;
        _pullRequestPolicyFailureNotifier = pullRequestPolicyFailureNotifier;
        _reminderManager = reminderManager;
        _logger = logger;

        _pullRequestUpdateState = new(stateManager, reminders, _logger, id.Id + StateConfiguration.PullRequestUpdateKey);
        _pullRequestCheckState = new(reminders, id.Id + StateConfiguration.PullRequestCheckKey);
        _pullRequestState = new(stateManager, _logger, id.Id + StateConfiguration.PullRequestKey);
        _codeFlowState = new(stateManager, _logger, id.Id + StateConfiguration.CodeFlowKey);
    }

    protected abstract Task<(string repository, string branch)> GetTargetAsync();

    protected abstract Task<IReadOnlyList<MergePolicyDefinition>> GetMergePolicyDefinitions();

    /// <summary>
    ///     Process any pending pull request updates stored in the <see cref="PullRequestUpdate" />
    ///     actor state key.
    /// </summary>
    /// <returns>
    ///     True if updates have been applied; <see langword="false" /> otherwise.
    /// </returns>
    public async Task<bool> ProcessPendingUpdatesAsync()
    {
        _logger.LogInformation("Processing pending updates");
        List<UpdateAssetsParameters>? updates = await _pullRequestUpdateState.TryGetStateAsync();
        if (updates == null || updates.Count < 1)
        {
            _logger.LogInformation("No Pending Updates");
            await _pullRequestUpdateState.UnsetReminderAsync();
            return false;
        }

        (InProgressPullRequest? pr, var canUpdate) = await SynchronizeInProgressPullRequestAsync();

        // Code flow updates are handled separetely
        if (updates.Any(u => u.Type == StateModel.SubscriptionType.DependenciesAndSources))
        {
            return await ProcessCodeFlowUpdatesAsync(updates, pr);
        }

        var subscriptionIds = updates.Count > 1
            ? "subscriptions " + string.Join(", ", updates.Select(u => u.SubscriptionId).Distinct())
            : "subscription " + updates[0].SubscriptionId;

        if (pr == null)
        {
            // Create regular dependency update PR
            var prUrl = await CreatePullRequestAsync(updates);

            if (prUrl == null)
            {
                _logger.LogInformation("No changes required for {subscriptions}, no pull request created", subscriptionIds);
            }
            else
            {
                _logger.LogInformation("Pull request '{url}' for {subscriptions} created", prUrl, subscriptionIds);
            }

            await _pullRequestUpdateState.RemoveStateAsync();
            await _pullRequestUpdateState.UnsetReminderAsync();

            return true;
        }

        if (!canUpdate)
        {
            _logger.LogInformation("PR {url} for {subscriptions} cannot be updated", pr.Url, subscriptionIds);
            return false;
        }

        await UpdatePullRequestAsync(pr, updates);
        _logger.LogInformation("Pull request {url} for {subscriptions} was updated", pr.Url, subscriptionIds);

        await _pullRequestUpdateState.RemoveStateAsync();
        await _pullRequestUpdateState.UnsetReminderAsync();

        return true;
    }

    protected virtual Task TagSourceRepositoryGitHubContactsIfPossibleAsync(InProgressPullRequest pr)
    {
        // Only do actual stuff in the non-batched implementation
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Synchronizes an in progress pull request.
    ///     This will update current state if the pull request has been manually closed or merged.
    ///     This will evaluate merge policies on an in progress pull request and merge the pull request if policies allow.
    /// </summary>
    /// <returns>
    ///     A <see cref="ValueTuple{InProgressPullRequest, bool}" /> containing:
    ///     The current open pull request if one exists, and
    ///     <see langword="true" /> if the open pull request can be updated; <see langword="false" /> otherwise.
    /// </returns>
    public virtual async Task<(InProgressPullRequest? pr, bool canUpdate)> SynchronizeInProgressPullRequestAsync()
    {
        InProgressPullRequest? pr = await _pullRequestState.TryGetStateAsync();

        if (pr == null)
        {
            _logger.LogInformation("No pull request state found. Stopping checks");
            await _pullRequestCheckState.UnsetReminderAsync();
            return (null, false);
        }

        if (string.IsNullOrEmpty(pr.Url))
        {
            // Somehow a bad PR got in the collection, remove it
            await _pullRequestState.RemoveStateAsync();
            await _codeFlowState.RemoveStateAsync();
            _logger.LogWarning("Removing invalid PR {url} from state memory", pr.Url);
            return (null, false);
        }

        SynchronizePullRequestResult result = await SynchronizePullRequestAsync(pr.Url);

        _logger.LogInformation("Pull request {url} is {result}", pr.Url, result);

        switch (result)
        {
            // If the PR was merged or closed, we are done with it and the actor doesn't
            // need to periodically run the synchronization any longer.
            case SynchronizePullRequestResult.Completed:
            case SynchronizePullRequestResult.UnknownPR:
                await _pullRequestCheckState.UnsetReminderAsync();
                return (null, false);
            case SynchronizePullRequestResult.InProgressCanUpdate:
                return (pr, true);
            case SynchronizePullRequestResult.InProgressCannotUpdate:
                return (pr, false);
            case SynchronizePullRequestResult.Invalid:
                // We could have gotten here if there was an exception during
                // the synchronization process. This was typical in the past
                // when we would regularly get credential exceptions on github tokens
                // that were just obtained. We don't want to unregister the reminder in these cases.
                return (null, false);
            default:
                _logger.LogError("Unknown pull request synchronization result {result}", result);
                await _pullRequestCheckState.UnsetReminderAsync();
                return (null, false);
        }
    }

    /// <summary>
    ///     Synchronizes a pull request
    /// </summary>
    /// <param name="prUrl">Pull request url.</param>
    /// <returns>
    ///    Result of the synchronization
    /// </returns>
    private async Task<SynchronizePullRequestResult> SynchronizePullRequestAsync(string prUrl)
    {
        _logger.LogInformation("Synchronizing pull request {prUrl}", prUrl);

        InProgressPullRequest? pr = await _pullRequestState.TryGetStateAsync();

        if (pr == null)
        {
            _logger.LogWarning("Invalid state detected for pull request '{prUrl}'", prUrl);
            await _pullRequestCheckState.UnsetReminderAsync();
            return SynchronizePullRequestResult.Invalid;
        }

        if (pr?.Url != prUrl)
        {
            _logger.LogInformation("Not Applicable: pull request {url} is not tracked by maestro anymore", prUrl);
            return SynchronizePullRequestResult.UnknownPR;
        }

        (var targetRepository, _) = await GetTargetAsync();
        IRemote remote = await _remoteFactory.GetRemoteAsync(targetRepository, _logger);

        _logger.LogInformation("Getting status for pull request: {url}", prUrl);
        PrStatus status = await remote.GetPullRequestStatusAsync(prUrl);
        _logger.LogInformation("Pull request {url} is {status}", prUrl, status);
        switch (status)
        {
            // If the PR is currently open, then evaluate the merge policies, which will potentially
            // merge the PR if they are successful.
            case PrStatus.Open:
                pr.MergePolicyResult = await CheckMergePolicyAsync(pr, remote);

                _logger.LogInformation("Policy check status for pull request {url} is {result}", prUrl, pr.MergePolicyResult);

                switch (pr.MergePolicyResult)
                {
                    case MergePolicyCheckResult.Merged:
                        await UpdateSubscriptionsForMergedPRAsync(pr.ContainedSubscriptions);
                        await AddDependencyFlowEventsAsync(
                            pr.ContainedSubscriptions,
                            DependencyFlowEventType.Completed,
                            DependencyFlowEventReason.AutomaticallyMerged,
                            pr.MergePolicyResult,
                            prUrl);

                        await _pullRequestState.RemoveStateAsync();
                        await _codeFlowState.RemoveStateAsync();

                        return SynchronizePullRequestResult.Completed;

                    case MergePolicyCheckResult.FailedPolicies:
                        await TagSourceRepositoryGitHubContactsIfPossibleAsync(pr);
                        goto case MergePolicyCheckResult.FailedToMerge;

                    case MergePolicyCheckResult.NoPolicies:
                    case MergePolicyCheckResult.FailedToMerge:
                        return SynchronizePullRequestResult.InProgressCanUpdate;

                    case MergePolicyCheckResult.PendingPolicies:
                        return SynchronizePullRequestResult.InProgressCannotUpdate;

                    default:
                        throw new NotImplementedException($"Unknown merge policy check result {pr.MergePolicyResult}");
                }

            case PrStatus.Merged:
            case PrStatus.Closed:
                // If the PR has been merged, update the subscription information
                if (status == PrStatus.Merged)
                {
                    await UpdateSubscriptionsForMergedPRAsync(pr.ContainedSubscriptions);
                }

                DependencyFlowEventReason reason = status == PrStatus.Merged
                    ? DependencyFlowEventReason.ManuallyMerged
                    : DependencyFlowEventReason.ManuallyClosed;

                await AddDependencyFlowEventsAsync(
                    pr.ContainedSubscriptions,
                    DependencyFlowEventType.Completed,
                    reason,
                    pr.MergePolicyResult,
                    prUrl);

                await _pullRequestState.RemoveStateAsync();
                await _codeFlowState.RemoveStateAsync();

                // Also try to clean up the PR branch.
                try
                {
                    _logger.LogInformation("Trying to clean up the branch for pull request {url}", prUrl);
                    await remote.DeletePullRequestBranchAsync(prUrl);
                }
                catch (DarcException)
                {
                    _logger.LogInformation("Failed to delete branch associated with pull request {url}", prUrl);
                }

                _logger.LogInformation("PR has been manually {action}", status);
                return SynchronizePullRequestResult.Completed; ;

            default:
                throw new NotImplementedException($"Unknown PR status '{status}'");
        }
    }

    /// <summary>
    ///     Check the merge policies for a PR and merge if they have succeeded.
    /// </summary>
    /// <param name="pr">Pull request</param>
    /// <param name="remote">Darc remote</param>
    /// <returns>Result of the policy check.</returns>
    private async Task<MergePolicyCheckResult> CheckMergePolicyAsync(IPullRequest pr, IRemote remote)
    {
        IReadOnlyList<MergePolicyDefinition> policyDefinitions = await GetMergePolicyDefinitions();
        MergePolicyEvaluationResults result = await _mergePolicyEvaluator.EvaluateAsync(pr, remote, policyDefinitions);

        await UpdateMergeStatusAsync(remote, pr.Url, result.Results);

        // As soon as one policy is actively failed, we enter a failed state.
        if (result.Failed)
        {
            _logger.LogInformation("NOT Merged: PR '{url}' failed policies {policies}",
                pr.Url,
                string.Join(", ", result.Results.Where(r => r.Status != MergePolicyEvaluationStatus.Success).Select(r => r.MergePolicyInfo.Name + r.Title)));

            return MergePolicyCheckResult.FailedPolicies;
        }

        if (result.Pending)
        {
            _logger.LogInformation("NOT Merged: PR '{url}' has pending policies {policies}",
                pr.Url,
                string.Join(", ", result.Results.Where(r => r.Status == MergePolicyEvaluationStatus.Pending).Select(r => r.MergePolicyInfo.Name + r.Title)));
            return MergePolicyCheckResult.PendingPolicies;
        }

        if (!result.Succeeded)
        {
            _logger.LogInformation("NOT Merged: PR '{url}' There are no merge policies", pr.Url);
            return MergePolicyCheckResult.NoPolicies;
        }

        try
        {
            await remote.MergeDependencyPullRequestAsync(pr.Url, new MergePullRequestParameters());
        }
        catch
        {
            _logger.LogInformation("NOT Merged: PR '{url}' has merge conflicts", pr.Url);
            return MergePolicyCheckResult.FailedToMerge;
        }

        var passedPolicies = string.Join(", ", policyDefinitions.Select(p => p.Name));
        _logger.LogInformation("Merged: PR '{url}' passed policies {passedPolicies}", pr.Url, passedPolicies);
        return MergePolicyCheckResult.Merged;
    }

    /// <summary>
    ///     Create new checks or update the status of existing checks for a PR.
    /// </summary>
    /// <param name="prUrl">Pull request URL</param>
    /// <param name="darc">Darc remote</param>
    /// <param name="evaluations">List of merge policies</param>
    /// <returns>Result of the policy check.</returns>
    private static Task UpdateMergeStatusAsync(IRemote darc, string prUrl, IReadOnlyList<MergePolicyEvaluationResult> evaluations)
    {
        return darc.CreateOrUpdatePullRequestMergeStatusInfoAsync(prUrl, evaluations);
    }

    private async Task UpdateSubscriptionsForMergedPRAsync(IEnumerable<SubscriptionPullRequestUpdate> subscriptionPullRequestUpdates)
    {
        _logger.LogInformation("Updating subscriptions for merged PR");
        foreach (SubscriptionPullRequestUpdate update in subscriptionPullRequestUpdates)
        {
            ISubscriptionActor actor = _actorFactory.CreateSubscriptionActor(update.SubscriptionId);
            if (!await actor.UpdateForMergedPullRequestAsync(update.BuildId))
            {
                _logger.LogInformation("Failed to update subscription {subscriptionId} for merged PR", update.SubscriptionId);
                await _pullRequestCheckState.UnsetReminderAsync();
                await _pullRequestUpdateState.UnsetReminderAsync();
                await _pullRequestState.RemoveStateAsync();
                await _codeFlowState.RemoveStateAsync();
            }
        }
    }

    private async Task AddDependencyFlowEventsAsync(
        IEnumerable<SubscriptionPullRequestUpdate> subscriptionPullRequestUpdates,
        DependencyFlowEventType flowEvent,
        DependencyFlowEventReason reason,
        MergePolicyCheckResult policy,
        string? prUrl)
    {
        foreach (SubscriptionPullRequestUpdate update in subscriptionPullRequestUpdates)
        {
            ISubscriptionActor actor = _actorFactory.CreateSubscriptionActor(update.SubscriptionId);
            if (!await actor.AddDependencyFlowEventAsync(update.BuildId, flowEvent, reason, policy, "PR", prUrl))
            {
                _logger.LogInformation("Failed to add dependency flow event for {subscriptionId}", update.SubscriptionId);
            }
        }
    }

    /// <summary>
    ///     Applies or queues asset updates for the target repository and branch from the given build and list of assets.
    /// </summary>
    /// <param name="subscriptionId">The id of the subscription the update comes from</param>
    /// <param name="buildId">The build that the updated assets came from</param>
    /// <param name="sourceSha">The commit hash that built the assets</param>
    /// <param name="assets">The list of assets</param>
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
    public async Task<bool> UpdateAssetsAsync(
        Guid subscriptionId,
        StateModel.SubscriptionType type,
        int buildId,
        string sourceRepo,
        string sourceSha,
        List<Asset> assets)
    {
        (InProgressPullRequest? pr, var canUpdate) = await SynchronizeInProgressPullRequestAsync();

        var updateParameter = new UpdateAssetsParameters
        {
            SubscriptionId = subscriptionId,
            Type = type,
            BuildId = buildId,
            SourceSha = sourceSha,
            SourceRepo = sourceRepo,
            Assets = assets,
            IsCoherencyUpdate = false,
        };

        // Regardless of code flow or regular PR, if the PR are not complete, postpone the update
        if (pr != null && !canUpdate)
        {
            await _pullRequestUpdateState.StoreItemStateAsync(updateParameter);
            await _pullRequestUpdateState.SetReminderAsync();
            _logger.LogInformation("Pull request '{prUrl}' cannot be updated, update queued", pr.Url);
            return true;
        }

        if (type == StateModel.SubscriptionType.DependenciesAndSources)
        {
            return await ProcessCodeFlowUpdatesAsync([updateParameter], pr);
        }

        try
        {
            if (pr == null)
            {
                var prUrl = await CreatePullRequestAsync([updateParameter]);
                if (prUrl == null)
                {
                    _logger.LogInformation("Updates require no changes, no pull request created");
                }
                else
                {
                    _logger.LogInformation("Pull request '{prUrl}' created", prUrl);
                }

                return true;
            }

            await UpdatePullRequestAsync(pr, [updateParameter]);
        }
        catch (HttpRequestException reqEx) when (reqEx.Message.Contains(((int)HttpStatusCode.Unauthorized).ToString()))
        {
            // We want to preserve the HttpRequestException's information but it's not serializable
            // We'll log the full exception object so it's in Application Insights, and strip any single quotes from the message to ensure 
            // GitHub issues are properly created.
            _logger.LogError(reqEx, "Failure to authenticate to repository");
            return false;
        }

        return true;
    }

    /// <summary>
    ///     Creates a pull request from the given updates.
    /// </summary>
    /// <returns>The pull request url when a pr was created; <see langref="null" /> if no PR is necessary</returns>
    private async Task<string?> CreatePullRequestAsync(List<UpdateAssetsParameters> updates)
    {
        (var targetRepository, var targetBranch) = await GetTargetAsync();

        IRemote darcRemote = await _remoteFactory.GetRemoteAsync(targetRepository, _logger);

        TargetRepoDependencyUpdate repoDependencyUpdate =
            await GetRequiredUpdates(updates, _remoteFactory, targetRepository, prBranch: null, targetBranch);

        if (repoDependencyUpdate.CoherencyCheckSuccessful && repoDependencyUpdate.RequiredUpdates.Count < 1)
        {
            return null;
        }

        var newBranchName = GetNewBranchName(targetBranch);
        await darcRemote.CreateNewBranchAsync(targetRepository, targetBranch, newBranchName);

        try
        {
            var description = await _pullRequestBuilder.CalculatePRDescriptionAndCommitUpdatesAsync(
                repoDependencyUpdate.RequiredUpdates,
                currentDescription: null,
                targetRepository,
                newBranchName);

            var inProgressPr = new InProgressPullRequest
            {
                // Calculate the subscriptions contained within the
                // update. Coherency updates do not have subscription info.
                ContainedSubscriptions = repoDependencyUpdate.RequiredUpdates
                        .Where(u => !u.update.IsCoherencyUpdate)
                        .Select(
                        u => new SubscriptionPullRequestUpdate
                        {
                            SubscriptionId = u.update.SubscriptionId,
                            BuildId = u.update.BuildId
                        })
                    .ToList(),

                RequiredUpdates = repoDependencyUpdate.RequiredUpdates
                        .SelectMany(update => update.deps)
                        .Select(du => new DependencyUpdateSummary
                        {
                            DependencyName = du.To.Name,
                            FromVersion = du.From.Version,
                            ToVersion = du.To.Version
                        })
                        .ToList(),

                CoherencyCheckSuccessful = repoDependencyUpdate.CoherencyCheckSuccessful,
                CoherencyErrors = repoDependencyUpdate.CoherencyErrors
            };

            var prUrl = await darcRemote.CreatePullRequestAsync(
                targetRepository,
                new PullRequest
                {
                    Title = await _pullRequestBuilder.GeneratePRTitleAsync(inProgressPr, targetBranch),
                    Description = description,
                    BaseBranch = targetBranch,
                    HeadBranch = newBranchName,
                });

            if (!string.IsNullOrEmpty(prUrl))
            {
                inProgressPr.Url = prUrl;

                await AddDependencyFlowEventsAsync(
                    inProgressPr.ContainedSubscriptions,
                    DependencyFlowEventType.Created,
                    DependencyFlowEventReason.New,
                    MergePolicyCheckResult.PendingPolicies,
                    prUrl);

                await _pullRequestState.StoreStateAsync(inProgressPr);
                await _pullRequestCheckState.SetReminderAsync();
                return prUrl;
            }

            // If we did not create a PR, then mark the dependency flow as completed as nothing to do.
            await AddDependencyFlowEventsAsync(
                    inProgressPr.ContainedSubscriptions,
                    DependencyFlowEventType.Completed,
                    DependencyFlowEventReason.NothingToDo,
                    MergePolicyCheckResult.PendingPolicies,
                    null);

            // Something wrong happened when trying to create the PR but didn't throw an exception (probably there was no diff).
            // We need to delete the branch also in this case.
            await darcRemote.DeleteBranchAsync(targetRepository, newBranchName);
            return null;
        }
        catch
        {
            await darcRemote.DeleteBranchAsync(targetRepository, newBranchName);
            throw;
        }
    }

    private async Task UpdatePullRequestAsync(InProgressPullRequest pr, List<UpdateAssetsParameters> updates)
    {
        (var targetRepository, var targetBranch) = await GetTargetAsync();

        _logger.LogInformation("Updating pull request {url} branch {targetBranch} in {targetRepository}", pr.Url, targetBranch, targetRepository);

        IRemote darcRemote = await _remoteFactory.GetRemoteAsync(targetRepository, _logger);
        PullRequest pullRequest = await darcRemote.GetPullRequestAsync(pr.Url);

        TargetRepoDependencyUpdate targetRepositoryUpdates =
            await GetRequiredUpdates(updates, _remoteFactory, targetRepository, pullRequest.HeadBranch, targetBranch);

        if (targetRepositoryUpdates.CoherencyCheckSuccessful && targetRepositoryUpdates.RequiredUpdates.Count < 1)
        {
            _logger.LogInformation("No updates found for pull request {url}", pr.Url);
            return;
        }

        _logger.LogInformation("Found {count} required updates for pull request {url}", targetRepositoryUpdates.RequiredUpdates.Count, pr.Url);

        pr.RequiredUpdates = MergeExistingWithIncomingUpdates(pr.RequiredUpdates, targetRepositoryUpdates.RequiredUpdates);

        if (pr.RequiredUpdates.Count < 1)
        {
            _logger.LogInformation("No new updates found for pull request {url}", pr.Url);
            return;
        }

        pr.CoherencyCheckSuccessful = targetRepositoryUpdates.CoherencyCheckSuccessful;
        pr.CoherencyErrors = targetRepositoryUpdates.CoherencyErrors;

        List<SubscriptionPullRequestUpdate> previousSubscriptions = [.. pr.ContainedSubscriptions];

        // Update the list of contained subscriptions with the new subscription update.
        // Replace all existing updates for the subscription id with the new update.
        // This avoids a potential issue where we may update the last applied build id
        // on the subscription to an older build id.
        foreach ((UpdateAssetsParameters update, List<DependencyUpdate> deps) update in targetRepositoryUpdates.RequiredUpdates)
        {
            pr.ContainedSubscriptions.RemoveAll(s => s.SubscriptionId == update.update.SubscriptionId);
        }

        // Mark all previous dependency updates that are being updated as Updated. All new dependencies should not be
        // marked as update as they are new. Any dependency not being updated should not be marked as failed.
        // At this point, pr.ContainedSubscriptions only contains the subscriptions that were not updated,
        // so everything that is in the previous list but not in the current list were updated.
        await AddDependencyFlowEventsAsync(
            previousSubscriptions.Except(pr.ContainedSubscriptions),
            DependencyFlowEventType.Updated,
            DependencyFlowEventReason.FailedUpdate,
            pr.MergePolicyResult,
            pr.Url);

        pr.ContainedSubscriptions.AddRange(targetRepositoryUpdates.RequiredUpdates
            .Where(u => !u.update.IsCoherencyUpdate)
            .Select(
                u => new SubscriptionPullRequestUpdate
                {
                    SubscriptionId = u.update.SubscriptionId,
                    BuildId = u.update.BuildId
                }));

        // Mark any new dependency updates as Created. Any subscriptions that are in pr.ContainedSubscriptions
        // but were not in the previous list of subscriptions are new
        await AddDependencyFlowEventsAsync(
            pr.ContainedSubscriptions.Except(previousSubscriptions),
            DependencyFlowEventType.Created,
            DependencyFlowEventReason.New,
            MergePolicyCheckResult.PendingPolicies,
            pr.Url);

        pullRequest.Description = await _pullRequestBuilder.CalculatePRDescriptionAndCommitUpdatesAsync(
            targetRepositoryUpdates.RequiredUpdates,
            pullRequest.Description,
            targetRepository,
            pullRequest.HeadBranch);

        pullRequest.Title = await _pullRequestBuilder.GeneratePRTitleAsync(pr, targetBranch);

        await darcRemote.UpdatePullRequestAsync(pr.Url, pullRequest);
        await _pullRequestState.StoreStateAsync(pr);
        await _pullRequestCheckState.SetReminderAsync();

        _logger.LogInformation("Pull request '{prUrl}' updated", pr.Url);
    }

    /// <summary>
    /// Merges the list of existing updates in a PR with a list of incoming updates
    /// </summary>
    /// <param name="existingUpdates">pr object to update</param>
    /// <param name="incomingUpdates">list of new incoming updates</param>
    /// <returns>Merged list of existing updates along with the new</returns>
    private static List<DependencyUpdateSummary> MergeExistingWithIncomingUpdates(
        List<DependencyUpdateSummary> existingUpdates,
        List<(UpdateAssetsParameters update, List<DependencyUpdate> deps)> incomingUpdates)
    {
        // First project the new updates to the final list
        var mergedUpdates =
            incomingUpdates.SelectMany(update => update.deps)
                .Select(du => new DependencyUpdateSummary
                {
                    DependencyName = du.To.Name,
                    FromVersion = du.From.Version,
                    ToVersion = du.To.Version
                }).ToList();

        // Project to a form that is easy to search
        var searchableUpdates =
            mergedUpdates.Select(u => u.DependencyName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Add any existing assets that weren't modified by the incoming update
        if (existingUpdates != null)
        {
            foreach (DependencyUpdateSummary update in existingUpdates)
            {
                if (!searchableUpdates.Contains(update.DependencyName))
                {
                    mergedUpdates.Add(update);
                }
            }
        }

        return mergedUpdates;
    }

    private class TargetRepoDependencyUpdate
    {
        public bool CoherencyCheckSuccessful { get; set; } = true;
        public List<CoherencyErrorDetails>? CoherencyErrors { get; set; }
        public List<(UpdateAssetsParameters update, List<DependencyUpdate> deps)> RequiredUpdates { get; set; } = [];
    }

    /// <summary>
    /// Given a set of input updates from builds, determine what updates
    /// are required in the target repository.
    /// </summary>
    /// <param name="updates">Updates</param>
    /// <param name="targetRepository">Target repository to calculate updates for</param>
    /// <param name="prBranch">PR head branch</param>
    /// <param name="targetBranch">Target branch</param>
    /// <param name="remoteFactory">Darc remote factory</param>
    /// <returns>List of updates and dependencies that need updates.</returns>
    /// <remarks>
    ///     This is done in two passes.  The first pass runs through and determines the non-coherency
    ///     updates required based on the input updates.  The second pass uses the repo state + the
    ///     updates from the first pass to determine what else needs to change based on the coherency metadata.
    /// </remarks>
    private async Task<TargetRepoDependencyUpdate> GetRequiredUpdates(
        List<UpdateAssetsParameters> updates,
        IRemoteFactory remoteFactory,
        string targetRepository,
        string? prBranch,
        string targetBranch)
    {
        _logger.LogInformation("Getting Required Updates for {branch} of {targetRepository}", targetBranch, targetRepository);
        // Get a remote factory for the target repo
        IRemote darc = await remoteFactory.GetRemoteAsync(targetRepository, _logger);

        TargetRepoDependencyUpdate repoDependencyUpdate = new();

        // Existing details 
        var existingDependencies = (await darc.GetDependenciesAsync(targetRepository, prBranch ?? targetBranch)).ToList();

        foreach (UpdateAssetsParameters update in updates)
        {
            IEnumerable<AssetData> assetData = update.Assets.Select(
                a => new AssetData(false)
                {
                    Name = a.Name,
                    Version = a.Version
                });
            // Retrieve the source of the assets

            List<DependencyUpdate> dependenciesToUpdate = _coherencyUpdateResolver.GetRequiredNonCoherencyUpdates(
                update.SourceRepo,
                update.SourceSha,
                assetData,
                existingDependencies);

            if (dependenciesToUpdate.Count < 1)
            {
                // No dependencies need to be updated.
                await UpdateSubscriptionsForMergedPRAsync(
                    new List<SubscriptionPullRequestUpdate>
                    {
                        new()
                        {
                            SubscriptionId = update.SubscriptionId,
                            BuildId = update.BuildId
                        }
                    });
                continue;
            }

            // Update the existing details list
            foreach (DependencyUpdate dependencyUpdate in dependenciesToUpdate)
            {
                existingDependencies.Remove(dependencyUpdate.From);
                existingDependencies.Add(dependencyUpdate.To);
            }
            repoDependencyUpdate.RequiredUpdates.Add((update, dependenciesToUpdate));
        }

        // Once we have applied all of non coherent updates, then we need to run a coherency check on the dependencies.
        List<DependencyUpdate> coherencyUpdates = [];
        try
        {
            _logger.LogInformation("Running a coherency check on the existing dependencies for branch {branch} of repo {repository}",
                targetBranch,
                targetRepository);
            coherencyUpdates = await _coherencyUpdateResolver.GetRequiredCoherencyUpdatesAsync(existingDependencies, remoteFactory);
        }
        catch (DarcCoherencyException e)
        {
            _logger.LogInformation("Failed attempting strict coherency update on branch '{strictCoherencyFailedBranch}' of repo '{strictCoherencyFailedRepo}'",
                 targetBranch, targetRepository);
            repoDependencyUpdate.CoherencyCheckSuccessful = false;
            repoDependencyUpdate.CoherencyErrors = e.Errors.Select(e => new CoherencyErrorDetails
            {
                Error = e.Error,
                PotentialSolutions = e.PotentialSolutions
            }).ToList();
        }

        if (coherencyUpdates.Count != 0)
        {
            // For the update asset parameters, we don't have any information on the source of the update,
            // since coherency can be run even without any updates.
            var coherencyUpdateParameters = new UpdateAssetsParameters
            {
                IsCoherencyUpdate = true
            };
            repoDependencyUpdate.RequiredUpdates.Add((coherencyUpdateParameters, coherencyUpdates.ToList()));
        }

        _logger.LogInformation("Finished getting Required Updates for {branch} of {targetRepository}", targetBranch, targetRepository);
        return repoDependencyUpdate;
    }

    private async Task<RepositoryBranchUpdate> GetRepositoryBranchUpdate()
    {
        (var repo, var branch) = await GetTargetAsync();
        RepositoryBranchUpdate? update = await _context.RepositoryBranchUpdates.FindAsync(repo, branch);
        if (update == null)
        {
            RepositoryBranch repoBranch = await GetRepositoryBranch(repo, branch);
            _context.RepositoryBranchUpdates.Add(
                update = new RepositoryBranchUpdate { RepositoryBranch = repoBranch });
        }
        else
        {
            _context.RepositoryBranchUpdates.Update(update);
        }

        return update;
    }

    private async Task<RepositoryBranch> GetRepositoryBranch(string repo, string branch)
    {
        RepositoryBranch? repoBranch = await _context.RepositoryBranches.FindAsync(repo, branch);
        if (repoBranch == null)
        {
            _context.RepositoryBranches.Add(
                repoBranch = new RepositoryBranch
                {
                    RepositoryName = repo,
                    BranchName = branch
                });
        }
        else
        {
            _context.RepositoryBranches.Update(repoBranch);
        }

        return repoBranch;
    }

    private static string GetNewBranchName(string targetBranch) => $"darc-{targetBranch}-{Guid.NewGuid()}";

    #region Code flow subscriptions

    /// <summary>
    /// Alternative to ProcessPendingUpdatesAsync that is used in the code flow (VMR) scenario.
    /// </summary>
    private async Task<bool> ProcessCodeFlowUpdatesAsync(
        List<UpdateAssetsParameters> updates,
        InProgressPullRequest? pr)
    {
        // TODO https://github.com/dotnet/arcade-services/issues/3378: Support batched PRs for code flow updates
        if (updates.Count > 1)
        {
            updates = [.. updates.DistinctBy(u => u.BuildId)];
            if (updates.Count > 1)
            {
                _logger.LogWarning("Code flow updates cannot be batched with other updates. Will process the last update only");
            }
        }

        var update = updates.Last();

        CodeFlowStatus? codeFlowStatus = await _codeFlowState.TryGetStateAsync();

        // The E2E order of things for is:
        // 1. We send a request to PCS and wait for a branch to be created. We note down this in the codeflow status. We set a reminder.
        // 2. When reminder kicks in, we check if the branch is created. If not, we repeat the reminder.
        // 3. When branch is created, we create the PR and set the usual reminder of watching a PR (common with the regular subscriptions).
        // 4. For new updates, we only delegate those to PCS which will push in the branch.
        if (pr == null)
        {
            (var targetRepository, var targetBranch) = await GetTargetAsync();

            // Step 1. Let PCS create a branch for us
            if (codeFlowStatus == null)
            {
                return await RequestCodeFlowBranchAsync(update, targetBranch);
            }

            // Step 2. Wait for the branch to be created
            IRemote remote = await _remoteFactory.GetRemoteAsync(targetRepository, _logger);
            if (!await remote.BranchExistsAsync(targetRepository, codeFlowStatus.PrBranch))
            {
                _logger.LogInformation("Branch {branch} for subscription {subscriptionId} not created yet. Will check again later",
                    codeFlowStatus.PrBranch,
                    update.SubscriptionId);

                await _pullRequestUpdateState.StoreStateAsync([update]);
                await _pullRequestUpdateState.SetReminderAsync(dueTimeInMinutes: 3);

                return true;
            }

            // Step 3. Create a PR
            var prUrl = await CreateCodeFlowPullRequestAsync(
                update,
                targetRepository,
                codeFlowStatus.PrBranch,
                targetBranch);

            _logger.LogInformation("Pending updates applied. PR {prUrl} created", prUrl);
            return true;
        }

        // Technically, this should never happen as we create the code flow data before we even create the PR
        if (codeFlowStatus == null)
        {
            _logger.LogError("Missing code flow data for subscription {subscription}", update.SubscriptionId);
            await _pullRequestUpdateState.RemoveStateAsync();
            await _pullRequestUpdateState.UnsetReminderAsync();
            await _pullRequestCheckState.UnsetReminderAsync();
            return false;
        }

        // Step 4. Update the PR (if needed)

        // Compare last SHA with the build SHA to see if we need to delegate this update to PCS
        if (update.SourceSha == codeFlowStatus.SourceSha)
        {
            _logger.LogInformation("PR {url} for {subscription} is up to date ({sha})",
                pr.Url,
                update.SubscriptionId,
                update.SourceSha);
            return false;
        }

        try
        {
            // TODO (https://github.com/dotnet/arcade-services/issues/3814): Post job to queue
            //await _pcsClient.CodeFlow.FlowAsync(new CodeFlowRequest
            //{
            //    BuildId = update.BuildId,
            //    SubscriptionId = update.SubscriptionId,
            //    PrBranch = codeFlowStatus.PrBranch,
            //    PrUrl = pr.Url,
            //});

            codeFlowStatus.SourceSha = update.SourceSha;

            await _codeFlowState.StoreStateAsync(codeFlowStatus);
            await _pullRequestState.StoreStateAsync(pr);
            await _pullRequestCheckState.SetReminderAsync();

            await _pullRequestUpdateState.RemoveStateAsync();
            await _pullRequestUpdateState.UnsetReminderAsync();
        }
        catch (Exception e)
        {
            // TODO https://github.com/dotnet/arcade-services/issues/3318: Handle this
            _logger.LogError(e, "Failed to request branch update for PR {url} for subscription {subscriptionId}",
                pr.Url,
                update.SubscriptionId);
        }

        _logger.LogInformation("New code flow changes requested");
        return true;
    }

    private async Task<bool> RequestCodeFlowBranchAsync(UpdateAssetsParameters update, string targetBranch)
    {
        CodeFlowStatus codeFlowUpdate = new()
        {
            PrBranch = GetNewBranchName(targetBranch),
            SourceSha = update.SourceSha,
        };

        _logger.LogInformation(
            "New code flow request for subscription {subscriptionId}. Requesting branch {branch} from PCS",
            update.SubscriptionId,
            codeFlowUpdate.PrBranch);

        try
        {
            // TODO (https://github.com/dotnet/arcade-services/issues/3814): Post job to queue
            //await _pcsClient.CodeFlow.FlowAsync(new CodeFlowRequest
            //{
            //    BuildId = update.BuildId,
            //    SubscriptionId = update.SubscriptionId,
            //    PrBranch = codeFlowUpdate.PrBranch,
            //});
        }
        catch (Exception e)
        {
            // TODO https://github.com/dotnet/arcade-services/issues/3318: Handle this
            _logger.LogError(e, "Failed to request new branch {branch} for subscription {subscriptionId}",
                codeFlowUpdate.PrBranch,
                update.SubscriptionId);
            return false;
        }

        await _codeFlowState.StoreStateAsync(codeFlowUpdate);
        await _pullRequestUpdateState.StoreItemStateAsync(update);
        await _pullRequestUpdateState.SetReminderAsync(dueTimeInMinutes: 3);

        _logger.LogInformation("Pending updates applied. Branch {prBranch} requested from PCS", codeFlowUpdate.PrBranch);
        return true;
    }

    private async Task<string> CreateCodeFlowPullRequestAsync(
        UpdateAssetsParameters update,
        string targetRepository,
        string prBranch,
        string targetBranch)
    {
        IRemote darcRemote = await _remoteFactory.GetRemoteAsync(targetRepository, _logger);

        try
        {
            var title = await _pullRequestBuilder.GenerateCodeFlowPRTitleAsync(update, targetBranch);
            var description = await _pullRequestBuilder.GenerateCodeFlowPRDescriptionAsync(update);

            var prUrl = await darcRemote.CreatePullRequestAsync(
                targetRepository,
                new PullRequest
                {
                    Title = title,
                    Description = description,
                    BaseBranch = targetBranch,
                    HeadBranch = prBranch,
                });

            InProgressPullRequest inProgressPr = new()
            {
                Url = prUrl,
                ContainedSubscriptions =
                [
                    new()
                    {
                        SubscriptionId = update.SubscriptionId,
                        BuildId = update.BuildId
                    }
                ]
            };

            await AddDependencyFlowEventsAsync(
                inProgressPr.ContainedSubscriptions,
                DependencyFlowEventType.Created,
                DependencyFlowEventReason.New,
                MergePolicyCheckResult.PendingPolicies,
                prUrl);

            await _pullRequestState.StoreStateAsync(inProgressPr);
            await _pullRequestCheckState.SetReminderAsync();

            await _pullRequestUpdateState.RemoveStateAsync();
            await _pullRequestUpdateState.UnsetReminderAsync();

            return prUrl;
        }
        catch
        {
            await darcRemote.DeleteBranchAsync(targetRepository, prBranch);
            throw;
        }
    }

    #endregion
}
