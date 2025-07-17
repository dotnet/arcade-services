// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using System.Text;
using Maestro.Data.Models;
using Maestro.DataProviders;
using Maestro.MergePolicies;
using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging;
using ProductConstructionService.Common;
using ProductConstructionService.DependencyFlow.Model;
using ProductConstructionService.DependencyFlow.WorkItems;
using ProductConstructionService.WorkItems;
using AssetData = Microsoft.DotNet.ProductConstructionService.Client.Models.AssetData;
using BuildDTO = Microsoft.DotNet.ProductConstructionService.Client.Models.Build;
using SubscriptionDTO = Microsoft.DotNet.ProductConstructionService.Client.Models.Subscription;

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

    private readonly IMergePolicyEvaluator _mergePolicyEvaluator;
    private readonly IRemoteFactory _remoteFactory;
    private readonly IPullRequestUpdaterFactory _updaterFactory;
    private readonly ICoherencyUpdateResolver _coherencyUpdateResolver;
    private readonly IPullRequestBuilder _pullRequestBuilder;
    private readonly IPullRequestConflictNotifier _pullRequestConflictNotifier;
    private readonly ISqlBarClient _sqlClient;
    private readonly ILocalLibGit2Client _gitClient;
    private readonly IVmrInfo _vmrInfo;
    private readonly IPcsVmrForwardFlower _vmrForwardFlower;
    private readonly IPcsVmrBackFlower _vmrBackFlower;
    private readonly ITelemetryRecorder _telemetryRecorder;
    private readonly ILogger _logger;

    private const string OverwrittenCommitMessage = "Stopping code flow updates for this pull request as the following commits would get overwritten:";

    protected readonly IReminderManager<SubscriptionUpdateWorkItem> _pullRequestUpdateReminders;
    protected readonly IReminderManager<PullRequestCheck> _pullRequestCheckReminders;
    protected readonly IRedisCache<InProgressPullRequest> _pullRequestState;
    protected readonly IRedisCache<MergePolicyEvaluationResults> _mergePolicyEvaluationState;

    public PullRequestUpdaterId Id { get; }

    public PullRequestUpdater(
        PullRequestUpdaterId id,
        IMergePolicyEvaluator mergePolicyEvaluator,
        IRemoteFactory remoteFactory,
        IPullRequestUpdaterFactory updaterFactory,
        ICoherencyUpdateResolver coherencyUpdateResolver,
        IPullRequestBuilder pullRequestBuilder,
        IPullRequestConflictNotifier pullRequestConflictNotifier,
        IRedisCacheFactory cacheFactory,
        IReminderManagerFactory reminderManagerFactory,
        ISqlBarClient sqlClient,
        ILocalLibGit2Client gitClient,
        IVmrInfo vmrInfo,
        IPcsVmrForwardFlower vmrForwardFlower,
        IPcsVmrBackFlower vmrBackFlower,
        ITelemetryRecorder telemetryRecorder,
        ILogger logger)
    {
        Id = id;
        _mergePolicyEvaluator = mergePolicyEvaluator;
        _remoteFactory = remoteFactory;
        _updaterFactory = updaterFactory;
        _coherencyUpdateResolver = coherencyUpdateResolver;
        _pullRequestBuilder = pullRequestBuilder;
        _pullRequestConflictNotifier = pullRequestConflictNotifier;
        _sqlClient = sqlClient;
        _gitClient = gitClient;
        _vmrInfo = vmrInfo;
        _vmrForwardFlower = vmrForwardFlower;
        _vmrBackFlower = vmrBackFlower;
        _telemetryRecorder = telemetryRecorder;
        _logger = logger;

        var cacheKey = id.ToString();
        _pullRequestUpdateReminders = reminderManagerFactory.CreateReminderManager<SubscriptionUpdateWorkItem>(cacheKey);
        _pullRequestCheckReminders = reminderManagerFactory.CreateReminderManager<PullRequestCheck>(cacheKey);
        _pullRequestState = cacheFactory.Create<InProgressPullRequest>(cacheKey);
        _mergePolicyEvaluationState = cacheFactory.Create<MergePolicyEvaluationResults>(cacheKey);
    }

    protected abstract Task<(string repository, string branch)> GetTargetAsync();

    protected abstract Task<IReadOnlyList<MergePolicyDefinition>> GetMergePolicyDefinitions();

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
        _logger.LogInformation("Processing pending updates for subscription {subscriptionId}", update.SubscriptionId);
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

            var pullRequest = await GetPullRequestStatusAsync(pr, isCodeFlow, tryingToUpdate: true);
            prInfo = pullRequest.PrInfo;
            switch (pullRequest.Status)
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
                    throw new NotImplementedException($"Unknown PR status {pullRequest.Status}");
            }
        }

        if (isCodeFlow)
        {
            await ProcessCodeFlowUpdateAsync(update, pr, prInfo, build, forceUpdate);
        }
        else 
        {
            await ProcessDependencyUpdateAsync(update, pr, prInfo, build);
        }
    }

    private async Task ProcessDependencyUpdateAsync(
        SubscriptionUpdateWorkItem update, 
        InProgressPullRequest? pr,
        PullRequest? prInfo,
        BuildDTO build)
    {
        if (pr != null && prInfo != null)
        {
            await UpdatePullRequestAsync(update, pr, prInfo, build);
            await _pullRequestUpdateReminders.UnsetReminderAsync(isCodeFlow: false);
            return;
        }

        // Create a new (regular) dependency update PR
        var prUrl = await CreatePullRequestAsync(update, build);
        if (prUrl == null)
        {
            _logger.LogInformation("No changes required for subscription {subscriptionId}, no pull request created", update.SubscriptionId);
        }
        else
        {
            _logger.LogInformation("Pull request '{url}' for subscription {subscriptionId} created", prUrl, update.SubscriptionId);
        }

        await _pullRequestUpdateReminders.UnsetReminderAsync(isCodeFlow: false);
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

    protected virtual async Task<bool> CheckInProgressPullRequestAsync(InProgressPullRequest pullRequestCheck, bool isCodeFlow)
    {
        _logger.LogInformation("Checking in-progress pull request {url}", pullRequestCheck.Url);
        var pr = await GetPullRequestStatusAsync(pullRequestCheck, isCodeFlow, tryingToUpdate: false);
        return pr.Status != PullRequestStatus.Invalid;
    }

    protected virtual Task TagSourceRepositoryGitHubContactsIfPossibleAsync(InProgressPullRequest pr)
    {
        // Only do actual stuff in the non-batched implementation
        return Task.CompletedTask;
    }

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
                        await ClearAllStateAsync(isCodeFlow, clearPendingUpdates: pr.MergeState == InProgressPullRequestState.Mergeable);
                        return (PullRequestStatus.Completed, prInfo);

                    case MergePolicyCheckResult.FailedPolicies:
                        await TagSourceRepositoryGitHubContactsIfPossibleAsync(pr);
                        goto case MergePolicyCheckResult.FailedToMerge;

                    case MergePolicyCheckResult.NoPolicies:
                    case MergePolicyCheckResult.FailedToMerge:
                        _logger.LogInformation("Pull request {url} still active (updatable) - keeping tracking it", pr.Url);
                        // Check if we think the PR has a conflict
                        if (pr.MergeState == InProgressPullRequestState.Conflict)
                        {
                            // If we think so, check if the PR head branch still has the same commit as the one we remembered.
                            // If it doesn't, we should try to update the PR again, the conflicts might be resolved
                            var latestCommit = await remote.GetLatestCommitAsync(targetRepository, pr.HeadBranch);
                            if (latestCommit == pr.SourceSha)
                            {
                                return (PullRequestStatus.InProgressCannotUpdate, prInfo);
                            }
                        }
                        // If we're about to update the PR, we should set the default reminder delay
                        await SetPullRequestCheckReminder(
                            pr,
                            isCodeFlow,
                            tryingToUpdate ?
                                DefaultReminderDelay :
                                GetReminderDelay(prInfo.UpdatedAt));
                        return (PullRequestStatus.InProgressCanUpdate, prInfo);

                    case MergePolicyCheckResult.PendingPolicies:
                        _logger.LogInformation("Pull request {url} still active (not updatable at the moment) - keeping tracking it", pr.Url);
                        await SetPullRequestCheckReminder(
                            pr,
                            isCodeFlow,
                            tryingToUpdate ?
                                DefaultReminderDelay :
                                GetReminderDelay(prInfo.UpdatedAt));
                        return (PullRequestStatus.InProgressCannotUpdate, prInfo);

                    default:
                        await SetPullRequestCheckReminder(
                            pr,
                            isCodeFlow,
                            tryingToUpdate ?
                                DefaultReminderDelay :
                                GetReminderDelay(prInfo.UpdatedAt));
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

                await ClearAllStateAsync(isCodeFlow, clearPendingUpdates: pr.MergeState == InProgressPullRequestState.Mergeable);

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
        (var targetRepository, var targetBranch) = await GetTargetAsync();
        IReadOnlyList<MergePolicyDefinition> policyDefinitions = await GetMergePolicyDefinitions();
        PullRequestUpdateSummary prSummary = CreatePrSummaryFromInProgressPr(pr, targetRepository);
        MergePolicyEvaluationResults? cachedResults = await _mergePolicyEvaluationState.TryGetStateAsync();

        IEnumerable<MergePolicyEvaluationResult> updatedMergePolicyResults = await _mergePolicyEvaluator.EvaluateAsync(prSummary, remote, policyDefinitions, cachedResults, prInfo.TargetBranchCommitSha);

        MergePolicyEvaluationResults updatedResult = new MergePolicyEvaluationResults(
            updatedMergePolicyResults.ToImmutableList(),
            prInfo.TargetBranchCommitSha);

        await _mergePolicyEvaluationState.SetAsync(updatedResult);

        await UpdateMergeStatusAsync(remote, pr.Url, updatedResult.Results);

        // As soon as one policy is actively failed, we enter a failed state.
        if (updatedResult.Failed)
        {
            _logger.LogInformation("NOT Merged: PR '{url}' failed policies {policies}",
                pr.Url,
                string.Join(Environment.NewLine, updatedResult.Results
                    .Where(r => r.Status is not MergePolicyEvaluationStatus.DecisiveSuccess or MergePolicyEvaluationStatus.TransientSuccess)
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

    private async Task UpdateSubscriptionsForMergedPRAsync(IEnumerable<SubscriptionPullRequestUpdate> subscriptionPullRequestUpdates)
    {
        _logger.LogInformation("Updating subscriptions for merged PR");
        foreach (SubscriptionPullRequestUpdate update in subscriptionPullRequestUpdates)
        {
            ISubscriptionTriggerer triggerer = _updaterFactory.CreateSubscriptionTrigerrer(update.SubscriptionId);
            if (!await triggerer.UpdateForMergedPullRequestAsync(update.BuildId))
            {
                _logger.LogWarning("Failed to update subscription {subscriptionId} for merged PR", update.SubscriptionId);
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
            ISubscriptionTriggerer triggerer = _updaterFactory.CreateSubscriptionTrigerrer(update.SubscriptionId);
            if (!await triggerer.AddDependencyFlowEventAsync(update.BuildId, flowEvent, reason, policy, "PR", prUrl))
            {
                _logger.LogInformation("Failed to add dependency flow event for {subscriptionId}", update.SubscriptionId);
            }
        }
    }

    /// <summary>
    ///     Creates a pull request from the given updates.
    /// </summary>
    /// <returns>The pull request url when a pr was created; <see langref="null" /> if no PR is necessary</returns>
    private async Task<string?> CreatePullRequestAsync(SubscriptionUpdateWorkItem update, BuildDTO build)
    {
        (var targetRepository, var targetBranch) = await GetTargetAsync();
        bool isCodeFlow = update.SubscriptionType == SubscriptionType.DependenciesAndSources;

        IRemote darcRemote = await _remoteFactory.CreateRemoteAsync(targetRepository);

        TargetRepoDependencyUpdate repoDependencyUpdate =
            await GetRequiredUpdates(update, targetRepository, build, prBranch: null, targetBranch: targetBranch);

        if (repoDependencyUpdate.CoherencyCheckSuccessful && repoDependencyUpdate.RequiredUpdates.Count < 1)
        {
            return null;
        }

        await RegisterSubscriptionUpdateAction(SubscriptionUpdateAction.ApplyingUpdates, update.SubscriptionId);

        var newBranchName = GetNewBranchName(targetBranch);
        await darcRemote.CreateNewBranchAsync(targetRepository, targetBranch, newBranchName);

        try
        {
            var description = await _pullRequestBuilder.CalculatePRDescriptionAndCommitUpdatesAsync(
                repoDependencyUpdate.RequiredUpdates,
                currentDescription: null,
                targetRepository,
                newBranchName);

            var containedSubscriptions = repoDependencyUpdate.RequiredUpdates
                // Coherency updates do not have subscription info.
                .Where(u => !u.update.IsCoherencyUpdate)
                .Select(
                u => new SubscriptionPullRequestUpdate
                {
                    SubscriptionId = u.update.SubscriptionId,
                    BuildId = u.update.BuildId,
                    SourceRepo = u.update.SourceRepo,
                    CommitSha = u.update.SourceSha
                })
                .ToList();

            var prUrl = await darcRemote.CreatePullRequestAsync(
                targetRepository,
                new PullRequest
                {
                    Title = await _pullRequestBuilder.GeneratePRTitleAsync(containedSubscriptions, targetBranch),
                    Description = description,
                    BaseBranch = targetBranch,
                    HeadBranch = newBranchName,
                });

            var inProgressPr = new InProgressPullRequest
            {
                UpdaterId = Id.ToString(),
                Url = prUrl,
                HeadBranch = newBranchName,
                SourceSha = update.SourceSha,

                ContainedSubscriptions = containedSubscriptions,

                RequiredUpdates = repoDependencyUpdate.RequiredUpdates
                        .SelectMany(update => update.deps)
                        .Select(du => new DependencyUpdateSummary(du))
                        .ToList(),

                CoherencyCheckSuccessful = repoDependencyUpdate.CoherencyCheckSuccessful,
                CoherencyErrors = repoDependencyUpdate.CoherencyErrors,
                CodeFlowDirection = CodeFlowDirection.None,
            };

            if (!string.IsNullOrEmpty(prUrl))
            {
                inProgressPr.Url = prUrl;

                await AddDependencyFlowEventsAsync(
                    inProgressPr.ContainedSubscriptions,
                    DependencyFlowEventType.Created,
                    DependencyFlowEventReason.New,
                    MergePolicyCheckResult.PendingPolicies,
                    prUrl);

                await SetPullRequestCheckReminder(inProgressPr, isCodeFlow);
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

    private async Task UpdatePullRequestAsync(
        SubscriptionUpdateWorkItem update,
        InProgressPullRequest pr,
        PullRequest prInfo,
        BuildDTO build)
    {
        (var targetRepository, var targetBranch) = await GetTargetAsync();

        _logger.LogInformation("Updating pull request {url} branch {targetBranch} in {targetRepository}", pr.Url, targetBranch, targetRepository);

        IRemote darcRemote = await _remoteFactory.CreateRemoteAsync(targetRepository);

        TargetRepoDependencyUpdate targetRepositoryUpdates =
            await GetRequiredUpdates(update, targetRepository, build, prInfo.HeadBranch, targetBranch);

        if (targetRepositoryUpdates.CoherencyCheckSuccessful && targetRepositoryUpdates.RequiredUpdates.Count < 1)
        {
            _logger.LogInformation("No updates found for pull request {url}", pr.Url);
            return;
        }

        _logger.LogInformation("Found {count} required updates for pull request {url}", targetRepositoryUpdates.RequiredUpdates.Count, pr.Url);

        pr.RequiredUpdates = MergeExistingWithIncomingUpdates(pr.RequiredUpdates,
            targetRepositoryUpdates.RequiredUpdates.SelectMany(x => x.deps).ToList());

        if (pr.RequiredUpdates.Count < 1)
        {
            _logger.LogInformation("No new updates found for pull request {url}", pr.Url);
            return;
        }

        await RegisterSubscriptionUpdateAction(SubscriptionUpdateAction.ApplyingUpdates, update.SubscriptionId);

        pr.CoherencyCheckSuccessful = targetRepositoryUpdates.CoherencyCheckSuccessful;
        pr.CoherencyErrors = targetRepositoryUpdates.CoherencyErrors;

        List<SubscriptionPullRequestUpdate> previousSubscriptions = [.. pr.ContainedSubscriptions];

        // Update the list of contained subscriptions with the new subscription update.
        // Replace all existing updates for the subscription id with the new update.
        // This avoids a potential issue where we may update the last applied build id
        // on the subscription to an older build id.
        pr.ContainedSubscriptions.RemoveAll(s => s.SubscriptionId == update.SubscriptionId);

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
                    BuildId = u.update.BuildId,
                    SourceRepo = u.update.SourceRepo,
                    CommitSha = u.update.SourceSha
                }));

        // Mark any new dependency updates as Created. Any subscriptions that are in pr.ContainedSubscriptions
        // but were not in the previous list of subscriptions are new
        await AddDependencyFlowEventsAsync(
            pr.ContainedSubscriptions.Except(previousSubscriptions),
            DependencyFlowEventType.Created,
            DependencyFlowEventReason.New,
            MergePolicyCheckResult.PendingPolicies,
            pr.Url);

        var requiredDescriptionUpdates =
            await CalculateOriginalDependencies(darcRemote, targetRepository, targetBranch, targetRepositoryUpdates);

        prInfo.Description = await _pullRequestBuilder.CalculatePRDescriptionAndCommitUpdatesAsync(
            requiredDescriptionUpdates,
            prInfo.Description,
            targetRepository,
            prInfo.HeadBranch);

        prInfo.Title = await _pullRequestBuilder.GeneratePRTitleAsync(pr.ContainedSubscriptions, targetBranch);

        await darcRemote.UpdatePullRequestAsync(pr.Url, prInfo);
        pr.LastUpdate = DateTime.UtcNow;
        pr.NextBuildsToProcess.Remove(update.SubscriptionId);
        await SetPullRequestCheckReminder(pr, isCodeFlow: update.SubscriptionType == SubscriptionType.DependenciesAndSources);

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
        List<DependencyUpdate> incomingUpdates)
    {
        IEnumerable<DependencyUpdateSummary> mergedUpdates = existingUpdates
            .Select(u =>
            {
                var matchingIncoming = incomingUpdates.FirstOrDefault(i => i.DependencyName == u.DependencyName);
                return new DependencyUpdateSummary()
                {
                    DependencyName = u.DependencyName,
                    FromCommitSha = u.FromCommitSha,
                    FromVersion = u.FromVersion,
                    ToCommitSha = matchingIncoming != null ? matchingIncoming.To.Commit : u.ToCommitSha,
                    ToVersion = matchingIncoming != null ? matchingIncoming.To.Version : u.ToVersion,
                };
            });

        IEnumerable<DependencyUpdateSummary> newUpdates = incomingUpdates
            .Where(u => !existingUpdates.Any(e => u.DependencyName == e.DependencyName))
            .Select(update => new DependencyUpdateSummary(update));

        return [.. mergedUpdates, .. newUpdates];
    }

    private class TargetRepoDependencyUpdate
    {
        public bool CoherencyCheckSuccessful { get; set; } = true;
        public List<CoherencyErrorDetails>? CoherencyErrors { get; set; }
        public List<(SubscriptionUpdateWorkItem update, List<DependencyUpdate> deps)> RequiredUpdates { get; set; } = [];
    }

    /// <summary>
    /// Given a set of input updates from builds, determine what updates
    /// are required in the target repository.
    /// </summary>
    /// <param name="update">Update</param>
    /// <param name="targetRepository">Target repository to calculate updates for</param>
    /// <param name="prBranch">PR head branch</param>
    /// <param name="targetBranch">Target branch</param>
    /// <returns>List of updates and dependencies that need updates.</returns>
    /// <remarks>
    ///     This is done in two passes.  The first pass runs through and determines the non-coherency
    ///     updates required based on the input updates.  The second pass uses the repo state + the
    ///     updates from the first pass to determine what else needs to change based on the coherency metadata.
    /// </remarks>
    private async Task<TargetRepoDependencyUpdate> GetRequiredUpdates(
        SubscriptionUpdateWorkItem update,
        string targetRepository,
        BuildDTO build,
        string? prBranch,
        string targetBranch)
    {
        _logger.LogInformation("Getting Required Updates for {branch} of {targetRepository}", targetBranch, targetRepository);
        // Get a remote factory for the target repo
        IRemote darc = await _remoteFactory.CreateRemoteAsync(targetRepository);

        TargetRepoDependencyUpdate repoDependencyUpdate = new();

        // Get subscription to access excluded assets
        var subscription = await _sqlClient.GetSubscriptionAsync(update.SubscriptionId)
            ?? throw new ($"Subscription with ID {update.SubscriptionId} not found in the DB.");

        var excludedAssetsMatcher = subscription.ExcludedAssets.GetAssetMatcher();

        // Existing details 
        var existingDependencies = (await darc.GetDependenciesAsync(targetRepository, prBranch ?? targetBranch)).ToList();

        // Filter out excluded assets from the build assets
        List<AssetData> assetData = build.Assets
            .Where(a => !excludedAssetsMatcher.IsExcluded(a.Name))
            .Select(a => new AssetData(false)
            {
                Name = a.Name,
                Version = a.Version
            })
            .ToList();

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
                        BuildId = update.BuildId,
                        SourceRepo = update.SourceRepo,
                        CommitSha = update.SourceSha
                    }
                });
        }
        else
        {
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
            coherencyUpdates = await _coherencyUpdateResolver.GetRequiredCoherencyUpdatesAsync(existingDependencies);
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
            var coherencyUpdateParameters = new SubscriptionUpdateWorkItem
            {
                UpdaterId = Id.Id,
                IsCoherencyUpdate = true
            };
            repoDependencyUpdate.RequiredUpdates.Add((coherencyUpdateParameters, coherencyUpdates.ToList()));
        }

        _logger.LogInformation("Finished getting Required Updates for {branch} of {targetRepository}", targetBranch, targetRepository);
        return repoDependencyUpdate;
    }

    private static string GetNewBranchName(string targetBranch) => $"darc-{targetBranch}-{Guid.NewGuid()}";

    private async Task SetPullRequestCheckReminder(InProgressPullRequest prState, bool isCodeFlow, TimeSpan reminderDelay)
    {
        var reminder = new PullRequestCheck()
        {
            UpdaterId = Id.ToString(),
            Url = prState.Url,
            IsCodeFlow = isCodeFlow
        };

        prState.LastCheck = DateTime.UtcNow;
        prState.NextCheck = prState.LastCheck + reminderDelay;

        await _pullRequestCheckReminders.SetReminderAsync(reminder, reminderDelay, isCodeFlow);
        await _pullRequestState.SetAsync(prState);
    }

   private async Task SetPullRequestCheckReminder(InProgressPullRequest prSate, bool isCodeFlow) =>
        await SetPullRequestCheckReminder(prSate, isCodeFlow, DefaultReminderDelay);

    private async Task ClearAllStateAsync(bool isCodeFlow, bool clearPendingUpdates)
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

    /// <summary>
    ///     Given a set of updates, replace the `from` version of every dependency update with the corresponding version
    ///     from the target branch 
    /// </summary>
    /// <param name="darcRemote">Darc client used to fetch target branch dependencies.</param>
    /// <param name="targetRepository">Target repository to fetch the dependencies from.</param>
    /// <param name="targetBranch">Target branch to fetch the dependencies from.</param>
    /// <param name="targetRepositoryUpdates">Incoming updates to the repository</param>
    /// <returns>
    ///     Subscription update and the corresponding list of altered dependencies
    /// </returns>
    /// <remarks>
    ///     This method is intended for use in situations where we want to keep the information about the original dependency
    ///     version, such as when updating PR descriptions.
    /// </remarks>
    private static async Task<List<(SubscriptionUpdateWorkItem update, List<DependencyUpdate> deps)>> CalculateOriginalDependencies(
        IRemote darcRemote,
        string targetRepository,
        string targetBranch,
        TargetRepoDependencyUpdate targetRepositoryUpdates)
    {
        List<DependencyDetail> targetBranchDeps = [.. await darcRemote.GetDependenciesAsync(targetRepository, targetBranch)];

        List<(SubscriptionUpdateWorkItem update, List<DependencyUpdate> deps)> alteredUpdates = [];
        foreach (var requiredUpdate in targetRepositoryUpdates.RequiredUpdates)
        {
            var updatedDependencies = requiredUpdate.deps
                .Select(dependency => new DependencyUpdate()
                {
                    From = targetBranchDeps
                            .Where(replace => dependency.From.Name == replace.Name)
                            .FirstOrDefault(dependency.From),
                    To = dependency.To,
                })
                .ToList();

            alteredUpdates.Add((requiredUpdate.update, updatedDependencies));
        }

        return alteredUpdates;
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

    private async Task RegisterSubscriptionUpdateAction(
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
            pr.ContainedSubscriptions.Select(su => new SubscriptionUpdateSummary(
                su.SubscriptionId,
                su.BuildId,
                su.SourceRepo,
                su.CommitSha)).ToList(),
            pr.HeadBranch,
            targetRepo,
            pr.CodeFlowDirection);
    }

    #region Code flow subscriptions

    /// <summary>
    /// Alternative to ProcessPendingUpdatesAsync that is used in the code flow (VMR) scenario.
    /// </summary>
    private async Task ProcessCodeFlowUpdateAsync(
        SubscriptionUpdateWorkItem update,
        InProgressPullRequest? pr,
        PullRequest? prInfo,
        BuildDTO build,
        bool forceUpdate)
    {
        if (update.SourceSha == pr?.SourceSha)
        {
            _logger.LogInformation("PR {url} for {subscription} is already up to date ({sha})",
                pr.Url,
                update.SubscriptionId,
                update.SourceSha);

            await SetPullRequestCheckReminder(pr, isCodeFlow: true);
            await _pullRequestUpdateReminders.UnsetReminderAsync(isCodeFlow: true);
            return;
        }

        var subscription = await _sqlClient.GetSubscriptionAsync(update.SubscriptionId);
        if (subscription == null)
        {
            _logger.LogWarning("Subscription {subscriptionId} was not found. Stopping updates", update.SubscriptionId);
            await ClearAllStateAsync(isCodeFlow: true, clearPendingUpdates: true);
            return;
        }

        var isForwardFlow = !string.IsNullOrEmpty(subscription.TargetDirectory);
        string prHeadBranch = pr?.HeadBranch ?? GetNewBranchName(subscription.TargetBranch);

        _logger.LogInformation(
            "{direction}-flowing build {buildId} for subscription {subscriptionId} targeting {repo} / {targetBranch} to new branch {newBranch}",
            isForwardFlow ? "Forward" : "Back",
            update.BuildId,
            subscription.Id,
            subscription.TargetRepository,
            subscription.TargetBranch,
            prHeadBranch);

        IRemote remote = await _remoteFactory.CreateRemoteAsync(subscription.TargetRepository);

        NativePath localRepoPath;
        CodeFlowResult codeFlowRes;
        IReadOnlyCollection<UpstreamRepoDiff> upstreamRepoDiffs;
        string? previousSourceSha; // is null in some edge cases like onboarding a new repository

        try
        {
            if (isForwardFlow)
            {
                codeFlowRes = await _vmrForwardFlower.FlowForwardAsync(
                    subscription,
                    build,
                    prHeadBranch,
                    skipMeaninglessUpdates: !forceUpdate,
                    cancellationToken: default);
                localRepoPath = _vmrInfo.VmrPath;

                SourceManifest? sourceManifest = await remote.GetSourceManifestAsync(
                    subscription.TargetRepository,
                    subscription.TargetBranch);

                previousSourceSha = sourceManifest?
                    .GetRepoVersion(subscription.TargetDirectory)?.CommitSha;

                upstreamRepoDiffs = [];
            }
            else
            {
                codeFlowRes = await _vmrBackFlower.FlowBackAsync(subscription, build, prHeadBranch, cancellationToken: default);
                localRepoPath = codeFlowRes.RepoPath;

                SourceDependency? sourceDependency = await remote.GetSourceDependencyAsync(
                    subscription.TargetRepository,
                    subscription.TargetBranch);

                previousSourceSha = sourceDependency?.Sha;

                upstreamRepoDiffs = await ComputeRepoUpdatesAsync(previousSourceSha, build.Commit);

                // Do not display the diff for which we're flowing back as the diff does not make a whole lot of sense
                upstreamRepoDiffs = [..upstreamRepoDiffs.Where(diff => diff.RepoUri != subscription.TargetRepository)];
            }
        }
        catch (ConflictInPrBranchException conflictException)
        {
            if (pr != null)
            {
                await HandlePrUpdateConflictAsync(conflictException.ConflictedFiles, update, subscription, pr, prHeadBranch);
            }
            return;
        }
        catch (TargetBranchNotFoundException)
        {
            if (pr != null)
            {
                // If PR already exists, this should not happen
                throw;
            }
            _logger.LogWarning("Target branch {targetBranch} not found for subscription {subscriptionId}.", 
                subscription.TargetBranch, 
                subscription.Id);
            return;
        }
        catch (ManualCommitsInFlowException exception)
        {
            if (pr != null)
            {
                // TODO https://github.com/dotnet/arcade-services/issues/5030
                // This is only a temporary band aid solution, we should figure out the best way to fix the algorithm so the flow continues as expected 
                await HandleOverwrittingChanges(subscription, exception.OverwrittenCommits, pr, update);
            }
            return;
        }
        catch (Exception)
        {
            _logger.LogError("Failed to flow source changes for build {buildId} in subscription {subscriptionId}",
                build.Id,
                subscription.Id);
            throw;
        }

        if (codeFlowRes.HadUpdates)
        {
            _logger.LogInformation("Code changes for {subscriptionId} ready in local branch {branch}",
                subscription.Id,
                prHeadBranch);

            using (var scope = _telemetryRecorder.RecordGitOperation(TrackedGitOperation.Push, subscription.TargetRepository))
            {
                await _gitClient.Push(localRepoPath, prHeadBranch, subscription.TargetRepository);
                scope.SetSuccess();
            }

            await RegisterSubscriptionUpdateAction(SubscriptionUpdateAction.ApplyingUpdates, update.SubscriptionId);
        }
        else
        {
            _logger.LogInformation("There were no code-flow updates for subscription {subscriptionId}", subscription.Id);
        }

        if (pr == null && codeFlowRes.HadUpdates)
        {
            pr = await CreateCodeFlowPullRequestAsync(
                update,
                previousSourceSha,
                subscription,
                prHeadBranch,
                codeFlowRes.DependencyUpdates,
                upstreamRepoDiffs,
                isForwardFlow);
        }
        else if (pr != null)
        {
            await UpdateCodeFlowPullRequestAsync(update,
                pr,
                prInfo,
                previousSourceSha,
                subscription,
                codeFlowRes.DependencyUpdates,
                upstreamRepoDiffs,
                isForwardFlow);

            _logger.LogInformation("Code flow update processed for pull request {prUrl}", pr.Url);
        }

        if (pr != null && codeFlowRes.ConflictedFiles.Count > 0)
        {
            await _pullRequestConflictNotifier.NotifyAboutMergeConflictAsync(
                pr,
                update,
                subscription,
                codeFlowRes.ConflictedFiles,
                build);
        }
    }

    /// <summary>
    /// Updates the PR's title and description
    /// </summary>
    private async Task UpdateCodeFlowPullRequestAsync(
        SubscriptionUpdateWorkItem update,
        InProgressPullRequest pullRequest,
        PullRequest? prInfo,
        string? previousSourceSha,
        SubscriptionDTO subscription,
        List<DependencyUpdate> newDependencyUpdates,
        IReadOnlyCollection<UpstreamRepoDiff>? upstreamRepoDiffs,
        bool isForwardFlow)
    {
        IRemote remote = await _remoteFactory.CreateRemoteAsync(subscription.TargetRepository);
        var build = await _sqlClient.GetBuildAsync(update.BuildId);

        pullRequest.ContainedSubscriptions.RemoveAll(s => s.SubscriptionId.Equals(update.SubscriptionId));
        pullRequest.ContainedSubscriptions.Add(new SubscriptionPullRequestUpdate
        {
            SubscriptionId = update.SubscriptionId,
            BuildId = update.BuildId,
            SourceRepo = update.SourceRepo,
            CommitSha = update.SourceSha
        });

        pullRequest.RequiredUpdates = MergeExistingWithIncomingUpdates(pullRequest.RequiredUpdates, newDependencyUpdates);

        var title = _pullRequestBuilder.GenerateCodeFlowPRTitle(
            subscription.TargetBranch,
            pullRequest.ContainedSubscriptions.Select(s => s.SourceRepo).ToList());

        var description = _pullRequestBuilder.GenerateCodeFlowPRDescription(
            update,
            build,
            previousSourceSha,
            pullRequest.RequiredUpdates,
            upstreamRepoDiffs,
            prInfo?.Description,
            isForwardFlow: isForwardFlow);

        try
        {
            await remote.UpdatePullRequestAsync(pullRequest.Url, new PullRequest
            {
                Title = title,
                Description = description
            });
        }
        catch (Exception e)
        {
            // If we get here, we already pushed the code updates, but failed to update things like the PR title and description
            // and enqueue a PullRequestCheck, so we'll just log a custom event for it
            _telemetryRecorder.RecordCustomEvent(CustomEventType.PullRequestUpdateFailed, new Dictionary<string, string>
                {
                    { "SubscriptionId", update.SubscriptionId.ToString() },
                    { "PullRequestUrl", pullRequest.Url }
                });
            _logger.LogError(e, "Failed to update PR {url} of subscription {subscriptionId}",
                pullRequest.Url,
                update.SubscriptionId);
        }
        finally
        {
            // Even if we fail to update the PR title and description, the changes already got pushed, so we want to enqueue a
            // PullRequestCheck
            pullRequest.SourceSha = update.SourceSha;
            pullRequest.LastUpdate = DateTime.UtcNow;
            pullRequest.MergeState = InProgressPullRequestState.Mergeable;
            pullRequest.NextBuildsToProcess.Remove(update.SubscriptionId);
            await SetPullRequestCheckReminder(pullRequest, isCodeFlow: true);
            await _pullRequestUpdateReminders.UnsetReminderAsync(isCodeFlow: true);
        }
    }

    private async Task<InProgressPullRequest> CreateCodeFlowPullRequestAsync(
        SubscriptionUpdateWorkItem update,
        string? previousSourceSha,
        SubscriptionDTO subscription,
        string prBranch,
        List<DependencyUpdate> dependencyUpdates,
        IReadOnlyCollection<UpstreamRepoDiff>? upstreamRepoDiffs,
        bool isForwardFlow)
    {
        IRemote darcRemote = await _remoteFactory.CreateRemoteAsync(subscription.TargetRepository);
        var build = await _sqlClient.GetBuildAsync(update.BuildId);
        List<DependencyUpdateSummary> requiredUpdates = dependencyUpdates.Select(du => new DependencyUpdateSummary(du)).ToList();
        try
        {
            var title = _pullRequestBuilder.GenerateCodeFlowPRTitle(subscription.TargetBranch, [update.SourceRepo]);
            var description = _pullRequestBuilder.GenerateCodeFlowPRDescription(
                update,
                build,
                previousSourceSha,
                requiredUpdates,
                upstreamRepoDiffs,
                currentDescription: null,
                isForwardFlow: isForwardFlow);

            var prUrl = await darcRemote.CreatePullRequestAsync(
                subscription.TargetRepository,
                new PullRequest
                {
                    Title = title,
                    Description = description,
                    BaseBranch = subscription.TargetBranch,
                    HeadBranch = prBranch,
                });

            InProgressPullRequest inProgressPr = new()
            {
                UpdaterId = Id.ToString(),
                Url = prUrl,
                HeadBranch = prBranch,
                SourceSha = update.SourceSha,
                ContainedSubscriptions =
                [
                    new SubscriptionPullRequestUpdate()
                    {
                        SubscriptionId = update.SubscriptionId,
                        BuildId = update.BuildId,
                        SourceRepo = update.SourceRepo,
                        CommitSha = update.SourceSha
                    }
                ],
                RequiredUpdates = requiredUpdates,
                CodeFlowDirection = !string.IsNullOrEmpty(subscription.TargetDirectory)
                    ? CodeFlowDirection.ForwardFlow
                    : CodeFlowDirection.BackFlow,
            };

            await AddDependencyFlowEventsAsync(
                inProgressPr.ContainedSubscriptions,
                DependencyFlowEventType.Created,
                DependencyFlowEventReason.New,
                MergePolicyCheckResult.PendingPolicies,
                prUrl);

            inProgressPr.LastUpdate = DateTime.UtcNow;
            await SetPullRequestCheckReminder(inProgressPr, isCodeFlow: true);
            await _pullRequestUpdateReminders.UnsetReminderAsync(isCodeFlow: true);

            _logger.LogInformation("Code flow pull request created: {prUrl}", prUrl);

            return inProgressPr;
        }
        catch (Exception)
        {
            _logger.LogError("Failed to create code flow pull request for subscription {subscriptionId}",
                update.SubscriptionId);
            await darcRemote.DeleteBranchAsync(subscription.TargetRepository, prBranch);
            throw;
        }
    }

    /// <summary>
    /// Handles a case when new code flow updates cannot be flowed into an existing PR,
    /// because the PR contains a change conflicting with the new updates.
    /// In this case, we post a comment on the PR with the list of files that are in conflict,
    /// </summary>
    private async Task HandlePrUpdateConflictAsync(
        List<string> filesInConflict,
        SubscriptionUpdateWorkItem update,
        SubscriptionDTO subscription,
        InProgressPullRequest pr,
        string prHeadBranch)
    {
        await _pullRequestConflictNotifier.NotifyAboutConflictingUpdateAsync(filesInConflict, update, subscription, pr, prHeadBranch);

        // If the headBranch gets updated, we will retry to update it with previously conflicting changes. If these changes still cause a conflict, we should update the
        // InProgressPullRequest with the latest commit from the remote branch
        var remoteCommit = pr.SourceSha;
        var remote = await _remoteFactory.CreateRemoteAsync(subscription.TargetRepository);
        try
        {
            remoteCommit = await remote.GetLatestCommitAsync(subscription.TargetRepository, pr.HeadBranch);
        }
        catch (Exception e)
        {
            _logger.LogWarning("Couldn't get latest commit of {repo}/{commit}. Failed with exception {message}",
                subscription.TargetRepository,
                pr.HeadBranch,
                e.Message);
        }

        pr.MergeState = InProgressPullRequestState.Conflict;
        pr.SourceSha = remoteCommit;
        pr.NextBuildsToProcess[update.SubscriptionId] = update.BuildId;
        await _pullRequestState.SetAsync(pr);
        await _pullRequestUpdateReminders.SetReminderAsync(update, DefaultReminderDelay, isCodeFlow: true);
        await _pullRequestCheckReminders.UnsetReminderAsync(isCodeFlow: true);
    }

    private async Task HandleOverwrittingChanges(SubscriptionDTO subscription, List<string> commits, InProgressPullRequest pr, SubscriptionUpdateWorkItem update)
    {
        var remote = await _remoteFactory.CreateRemoteAsync(subscription.TargetRepository);
        var prComments = await remote.GetPullRequestCommentsAsync(pr.Url);
        if (!prComments.Any(c => c.Contains(OverwrittenCommitMessage)))
        {
            _logger.LogInformation(
                "Codeflow would overwrite manual PR changes. Stoping updates for subscription {subscriptionId} until the PR is merged",
                update.SubscriptionId);
            StringBuilder sb = new();
            sb.AppendLine(OverwrittenCommitMessage);
            foreach (var commit in commits)
            {
                sb.AppendLine($"- {commit}");
            }
            sb.AppendLine();
            sb.AppendLine("Codeflow will resume after this PR is merged");
            await remote.CommentPullRequestAsync(pr.Url, sb.ToString());
        }
        else
        {
            _logger.LogInformation(
                "Codeflow for subscription {subscriptionId} is already stopped as it would overwrite manual changes",
                update.SubscriptionId);
        }

            pr.NextBuildsToProcess[update.SubscriptionId] = update.BuildId;
        await _pullRequestState.SetAsync(pr);
        await _pullRequestUpdateReminders.SetReminderAsync(update, DefaultReminderDelay, isCodeFlow: true);
        await _pullRequestCheckReminders.UnsetReminderAsync(isCodeFlow: true);
    }

    // <summary>
    // Returns the commit-diffs in all product repositories between the last flow SHA and the current flow SHA.
    // </summary>
    private async Task<IReadOnlyCollection<UpstreamRepoDiff>> ComputeRepoUpdatesAsync(string? previousFlowSha, string currentFlowSha)
    {
        _logger.LogInformation("Computing repo updates between {LastFlowSha} and {CurrentFlowSha}", previousFlowSha, currentFlowSha);

        if (string.IsNullOrEmpty(previousFlowSha))
        {
            _logger.LogWarning("Aborting repo diff calculation: previousFlowSha is null.");
            return [];
        }

        string oldFileContents = await _gitClient.GetFileFromGitAsync(_vmrInfo.VmrPath, VmrInfo.DefaultRelativeSourceManifestPath, previousFlowSha)
            ?? throw new DependencyFileNotFoundException($"Could not find {VmrInfo.DefaultRelativeSourceManifestPath} in {_vmrInfo.VmrPath} at commit {previousFlowSha}");

        string newFileContents = await _gitClient.GetFileFromGitAsync(_vmrInfo.VmrPath, VmrInfo.DefaultRelativeSourceManifestPath, currentFlowSha)
            ?? throw new DependencyFileNotFoundException($"Could not find {VmrInfo.DefaultRelativeSourceManifestPath} in {_vmrInfo.VmrPath} at commit {currentFlowSha}");

        SourceManifest oldSrcManifest = SourceManifest.FromJson(oldFileContents);
        SourceManifest newSrcManifest = SourceManifest.FromJson(newFileContents);

        var oldRepos = oldSrcManifest.Repositories.ToDictionary(r => r.RemoteUri, r => r.CommitSha);
        var newRepos = newSrcManifest.Repositories.ToDictionary(r => r.RemoteUri, r => r.CommitSha);

        var allKeys = oldRepos.Keys.Union(newRepos.Keys);

        var upstreamRepoDiffs = allKeys
            .Select(key => new UpstreamRepoDiff(
                key,
                oldRepos.TryGetValue(key, out var oldSha) ? oldSha : null,
                newRepos.TryGetValue(key, out var newSha) ? newSha : null))
            .Where(x => x.OldCommitSha != x.NewCommitSha)
            .ToList();

        return upstreamRepoDiffs;
        
    }

    #endregion
}

// <summary>
// Contains the old and new SHAs of an upstream repo (repo that the product repo depends on)
// </summary>
public record UpstreamRepoDiff(
    string RepoUri,
    string? OldCommitSha,
    string? NewCommitSha);
