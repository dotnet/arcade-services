// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data.Models;
using Maestro.MergePolicies;
using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging;
using ProductConstructionService.Common;
using ProductConstructionService.DependencyFlow.Model;
using ProductConstructionService.DependencyFlow.WorkItems;
using ProductConstructionService.WorkItems;

using Asset = ProductConstructionService.DependencyFlow.Model.Asset;
using AssetData = Microsoft.DotNet.ProductConstructionService.Client.Models.AssetData;

namespace ProductConstructionService.DependencyFlow;

/// <summary>
///     A class responsible for creating and updating pull requests for dependency updates.
/// </summary>
internal abstract class PullRequestUpdater : IPullRequestUpdater
{
#if DEBUG
    private static readonly TimeSpan DefaultReminderDelay = TimeSpan.FromMinutes(3);
#else
    private static readonly TimeSpan DefaultReminderDelay = TimeSpan.FromMinutes(5);
#endif

    private readonly IMergePolicyEvaluator _mergePolicyEvaluator;
    private readonly IRemoteFactory _remoteFactory;
    private readonly IPullRequestUpdaterFactory _updaterFactory;
    private readonly ICoherencyUpdateResolver _coherencyUpdateResolver;
    private readonly IPullRequestBuilder _pullRequestBuilder;
    private readonly IBasicBarClient _barClient;
    private readonly ILocalLibGit2Client _gitClient;
    private readonly IVmrInfo _vmrInfo;
    private readonly IPcsVmrForwardFlower _vmrForwardFlower;
    private readonly IPcsVmrBackFlower _vmrBackFlower;
    private readonly ITelemetryRecorder _telemetryRecorder;
    private readonly ILogger _logger;

    protected readonly IReminderManager<SubscriptionUpdateWorkItem> _pullRequestUpdateReminders;
    protected readonly IReminderManager<PullRequestCheck> _pullRequestCheckReminders;
    protected readonly IRedisCache<InProgressPullRequest> _pullRequestState;

    public PullRequestUpdaterId Id { get; }

    public PullRequestUpdater(
        PullRequestUpdaterId id,
        IMergePolicyEvaluator mergePolicyEvaluator,
        IRemoteFactory remoteFactory,
        IPullRequestUpdaterFactory updaterFactory,
        ICoherencyUpdateResolver coherencyUpdateResolver,
        IPullRequestBuilder pullRequestBuilder,
        IRedisCacheFactory cacheFactory,
        IReminderManagerFactory reminderManagerFactory,
        IBasicBarClient barClient,
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
        _barClient = barClient;
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
    }

    protected abstract Task<(string repository, string branch)> GetTargetAsync();

    protected abstract Task<IReadOnlyList<MergePolicyDefinition>> GetMergePolicyDefinitions();

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
        SubscriptionType type,
        int buildId,
        string sourceRepo,
        string sourceSha,
        List<Asset> assets)
    {
        return await ProcessPendingUpdatesAsync(new()
        {
            UpdaterId = Id.ToString(),
            SubscriptionId = subscriptionId,
            SubscriptionType = type,
            BuildId = buildId,
            SourceSha = sourceSha,
            SourceRepo = sourceRepo,
            Assets = assets,
            IsCoherencyUpdate = false,
        });
    }

    /// <summary>
    ///     Process any pending pull request updates.
    /// </summary>
    /// <returns>
    ///     True if updates have been applied; <see langword="false" /> otherwise.
    /// </returns>
    public async Task<bool> ProcessPendingUpdatesAsync(SubscriptionUpdateWorkItem update)
    {
        _logger.LogInformation("Processing pending updates for subscription {subscriptionId}", update.SubscriptionId);

        // Check if we track an on-going PR already
        InProgressPullRequest? pr = await _pullRequestState.TryGetStateAsync();
        bool isCodeFlow = update.SubscriptionType == SubscriptionType.DependenciesAndSources;

        if (pr == null)
        {
            _logger.LogInformation("No existing pull request state found");
        }
        else
        {
            var prStatus = await GetPullRequestStatusAsync(pr, isCodeFlow);
            switch (prStatus)
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
                    _logger.LogInformation("PR {url} for subscription {subscriptionId} cannot be updated at this time. Deferring update..", pr.Url, update.SubscriptionId);
                    await _pullRequestUpdateReminders.SetReminderAsync(update, DefaultReminderDelay, isCodeFlow);
                    await _pullRequestCheckReminders.UnsetReminderAsync(isCodeFlow);
                    return false;
                default:
                    throw new NotImplementedException($"Unknown PR status {prStatus}");
            }
        }

        // Code flow updates are handled separetely
        if (isCodeFlow)
        {
            return await ProcessCodeFlowUpdateAsync(update, pr);
        }

        // If we have an existing PR, update it
        if (pr != null)
        {
            await UpdatePullRequestAsync(pr, update);
            await _pullRequestUpdateReminders.UnsetReminderAsync(isCodeFlow);
            return true;
        }

        // Create a new (regular) dependency update PR
        var prUrl = await CreatePullRequestAsync(update);
        if (prUrl == null)
        {
            _logger.LogInformation("No changes required for subscription {subscriptionId}, no pull request created", update.SubscriptionId);
        }
        else
        {
            _logger.LogInformation("Pull request '{url}' for subscription {subscriptionId} created", prUrl, update.SubscriptionId);
        }

        await _pullRequestUpdateReminders.UnsetReminderAsync(isCodeFlow);
        return true;
    }

    public async Task<bool> CheckPullRequestAsync(PullRequestCheck pullRequestCheck)
    {
        var inProgressPr = await _pullRequestState.TryGetStateAsync();

        if (inProgressPr == null)
        {
            _logger.LogInformation("No in-progress pull request found for a PR check");
            await ClearAllStateAsync(isCodeFlow: true);
            await ClearAllStateAsync(isCodeFlow: false);
            return false;
        }

        return await CheckInProgressPullRequestAsync(inProgressPr, pullRequestCheck.IsCodeFlow);
    }

    protected virtual async Task<bool> CheckInProgressPullRequestAsync(InProgressPullRequest pullRequestCheck, bool isCodeFlow)
    {
        _logger.LogInformation("Checking in-progress pull request {url}", pullRequestCheck.Url);
        var status = await GetPullRequestStatusAsync(pullRequestCheck, isCodeFlow);
        return status != PullRequestStatus.Invalid;
    }

    protected virtual Task TagSourceRepositoryGitHubContactsIfPossibleAsync(InProgressPullRequest pr)
    {
        // Only do actual stuff in the non-batched implementation
        return Task.CompletedTask;
    }

    protected async Task<PullRequestStatus?> GetPullRequestStatusAsync(InProgressPullRequest pr, bool isCodeFlow)
    {
        _logger.LogInformation("Querying status for pull request {prUrl}", pr.Url);

        (var targetRepository, _) = await GetTargetAsync();
        IRemote remote = await _remoteFactory.CreateRemoteAsync(targetRepository);

        PrStatus status;
        try
        {
            status = await remote.GetPullRequestStatusAsync(pr.Url);
        }
        catch (Exception)
        {
            _logger.LogError($"Couldn't get status of PR {pr.Url}");
            throw;
        }
        _logger.LogInformation("Pull request {url} is {status}", pr.Url, status);

        switch (status)
        {
            // If the PR is currently open, then evaluate the merge policies, which will potentially
            // merge the PR if they are successful.
            case PrStatus.Open:
                MergePolicyCheckResult mergePolicyResult = await TryMergingPrAsync(pr, remote);

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

                        await ClearAllStateAsync(isCodeFlow);
                        return PullRequestStatus.Completed;

                    case MergePolicyCheckResult.FailedPolicies:
                        await TagSourceRepositoryGitHubContactsIfPossibleAsync(pr);
                        goto case MergePolicyCheckResult.FailedToMerge;

                    case MergePolicyCheckResult.NoPolicies:
                    case MergePolicyCheckResult.FailedToMerge:
                        _logger.LogInformation("Pull request {url} still active (updatable) - keeping tracking it", pr.Url);
                        await SetPullRequestCheckReminder(pr, isCodeFlow);
                        return PullRequestStatus.InProgressCanUpdate;

                    case MergePolicyCheckResult.PendingPolicies:
                        _logger.LogInformation("Pull request {url} still active (not updatable at the moment) - keeping tracking it", pr.Url);
                        await SetPullRequestCheckReminder(pr, isCodeFlow);
                        return PullRequestStatus.InProgressCannotUpdate;

                    default:
                        await SetPullRequestCheckReminder(pr, isCodeFlow);
                        throw new NotImplementedException($"Unknown merge policy check result {mergePolicyResult}");
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
                    pr.Url);

                _logger.LogInformation("PR {url} has been manually {action}. Stopping tracking it", pr.Url, status.ToString().ToLowerInvariant());

                await ClearAllStateAsync(isCodeFlow);

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

                return PullRequestStatus.Completed;

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
    private async Task<MergePolicyCheckResult> TryMergingPrAsync(InProgressPullRequest pr, IRemote remote)
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

        var passedPolicies = string.Join(", ", policyDefinitions.Select(p => p.Name));
        _logger.LogInformation("Merged: PR '{url}' passed policies {passedPolicies}", pr.Url, passedPolicies);
        return MergePolicyCheckResult.Merged;
    }

    /// <summary>
    ///     Create new checks or update the status of existing checks for a PR.
    /// </summary>
    /// <param name="prUrl">Pull request URL</param>
    /// <param name="remote">Darc remote</param>
    /// <param name="evaluations">List of merge policies</param>
    /// <returns>Result of the policy check.</returns>
    private static Task UpdateMergeStatusAsync(IRemote remote, string prUrl, IReadOnlyList<MergePolicyEvaluationResult> evaluations)
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
    private async Task<string?> CreatePullRequestAsync(SubscriptionUpdateWorkItem update)
    {
        (var targetRepository, var targetBranch) = await GetTargetAsync();
        bool isCodeFlow = update.SubscriptionType == SubscriptionType.DependenciesAndSources;

        IRemote darcRemote = await _remoteFactory.CreateRemoteAsync(targetRepository);

        TargetRepoDependencyUpdate repoDependencyUpdate =
            await GetRequiredUpdates(update, _remoteFactory, targetRepository, prBranch: null, targetBranch);

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

            var containedSubscriptions = repoDependencyUpdate.RequiredUpdates
                // Coherency updates do not have subscription info.
                .Where(u => !u.update.IsCoherencyUpdate)
                .Select(
                u => new SubscriptionPullRequestUpdate
                {
                    SubscriptionId = u.update.SubscriptionId,
                    BuildId = u.update.BuildId
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
                        .Select(du => new DependencyUpdateSummary
                        {
                            DependencyName = du.To.Name,
                            FromVersion = du.From.Version,
                            ToVersion = du.To.Version
                        })
                        .ToList(),

                CoherencyCheckSuccessful = repoDependencyUpdate.CoherencyCheckSuccessful,
                CoherencyErrors = repoDependencyUpdate.CoherencyErrors,
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

    private async Task UpdatePullRequestAsync(InProgressPullRequest pr, SubscriptionUpdateWorkItem update)
    {
        (var targetRepository, var targetBranch) = await GetTargetAsync();

        _logger.LogInformation("Updating pull request {url} branch {targetBranch} in {targetRepository}", pr.Url, targetBranch, targetRepository);

        IRemote darcRemote = await _remoteFactory.CreateRemoteAsync(targetRepository);
        PullRequest pullRequest = await darcRemote.GetPullRequestAsync(pr.Url);

        TargetRepoDependencyUpdate targetRepositoryUpdates =
            await GetRequiredUpdates(update, _remoteFactory, targetRepository, pullRequest.HeadBranch, targetBranch);

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

        var requiredDescriptionUpdates =
            await CalculateOriginalDependencies(darcRemote, targetRepository, targetBranch, targetRepositoryUpdates);

        pullRequest.Description = await _pullRequestBuilder.CalculatePRDescriptionAndCommitUpdatesAsync(
            requiredDescriptionUpdates,
            pullRequest.Description,
            targetRepository,
            pullRequest.HeadBranch);

        pullRequest.Title = await _pullRequestBuilder.GeneratePRTitleAsync(pr.ContainedSubscriptions, targetBranch);

        await darcRemote.UpdatePullRequestAsync(pr.Url, pullRequest);
        pr.LastUpdate = DateTime.UtcNow;
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
        List<(SubscriptionUpdateWorkItem update, List<DependencyUpdate> deps)> incomingUpdates)
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
    /// <param name="remoteFactory">Darc remote factory</param>
    /// <returns>List of updates and dependencies that need updates.</returns>
    /// <remarks>
    ///     This is done in two passes.  The first pass runs through and determines the non-coherency
    ///     updates required based on the input updates.  The second pass uses the repo state + the
    ///     updates from the first pass to determine what else needs to change based on the coherency metadata.
    /// </remarks>
    private async Task<TargetRepoDependencyUpdate> GetRequiredUpdates(
        SubscriptionUpdateWorkItem update,
        IRemoteFactory remoteFactory,
        string targetRepository,
        string? prBranch,
        string targetBranch)
    {
        _logger.LogInformation("Getting Required Updates for {branch} of {targetRepository}", targetBranch, targetRepository);
        // Get a remote factory for the target repo
        IRemote darc = await remoteFactory.CreateRemoteAsync(targetRepository);

        TargetRepoDependencyUpdate repoDependencyUpdate = new();

        // Existing details 
        var existingDependencies = (await darc.GetDependenciesAsync(targetRepository, prBranch ?? targetBranch)).ToList();

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

    private async Task SetPullRequestCheckReminder(InProgressPullRequest prState, bool isCodeFlow)
    {
        var reminder = new PullRequestCheck()
        {
            UpdaterId = Id.ToString(),
            Url = prState.Url,
            IsCodeFlow = isCodeFlow
        };

        prState.LastCheck = DateTime.UtcNow;
        prState.NextCheck = prState.LastCheck + DefaultReminderDelay;

        await _pullRequestCheckReminders.SetReminderAsync(reminder, DefaultReminderDelay, isCodeFlow);
        await _pullRequestState.SetAsync(prState);
    }

    private async Task ClearAllStateAsync(bool isCodeFlow)
    {
        await _pullRequestState.TryDeleteAsync();
        await _pullRequestCheckReminders.UnsetReminderAsync(isCodeFlow);
        await _pullRequestUpdateReminders.UnsetReminderAsync(isCodeFlow);
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

    #region Code flow subscriptions

    /// <summary>
    /// Alternative to ProcessPendingUpdatesAsync that is used in the code flow (VMR) scenario.
    /// </summary>
    private async Task<bool> ProcessCodeFlowUpdateAsync(
        SubscriptionUpdateWorkItem update,
        InProgressPullRequest? pr)
    {
        bool isCodeFlow = update.SubscriptionType == SubscriptionType.DependenciesAndSources;
        // Compare last SHA with the build SHA to see if we already have this SHA in the PR
        if (update.SourceSha == pr?.SourceSha)
        {
            _logger.LogInformation("PR {url} for {subscription} is already up to date ({sha})",
                pr.Url,
                update.SubscriptionId,
                update.SourceSha);

            await SetPullRequestCheckReminder(pr, isCodeFlow);
            await _pullRequestUpdateReminders.UnsetReminderAsync(isCodeFlow: true);

            return true;
        }

        // The E2E order of things for is:
        // 1. We send a request to PCS and wait for a branch to be created. We note down this in the codeflow status. We set a reminder.
        // 2. When reminder kicks in, we check if the branch is created. If not, we repeat the reminder.
        // 3. When branch is created, we create the PR and set the usual reminder of watching a PR (common with the regular subscriptions).
        // 4. For new updates, we only delegate those to PCS which will push in the branch.
        if (pr == null)
        {
            (var targetRepository, var targetBranch) = await GetTargetAsync();

            // Step 1. Create a branch for us
            var prBranch = await CreateCodeFlowBranchAsync(update, targetBranch);
            if (prBranch == null)
            {
                _logger.LogInformation("No changes required for subscription {subscriptionId}, no pull request created", update.SubscriptionId);
                return true;
            }

            // Step 2. Create a PR
            await CreateCodeFlowPullRequestAsync(
                update,
                targetRepository,
                prBranch,
                targetBranch);

            return true;
        }

        // Step 3. Update the PR
        try
        {
            await UpdateAssetsAndSources(update, pr);
        }
        catch (Exception e)
        {
            // TODO https://github.com/dotnet/arcade-services/issues/4198: Notify us about these kind of failures
            _logger.LogError(e, "Failed to update sources and packages for PR {url} of subscription {subscriptionId}",
                pr.Url,
                update.SubscriptionId);
            return false;
        }

        _logger.LogInformation("Code flow update processed for pull request {prUrl}", pr.Url);
        return true;
    }

    /// <summary>
    /// Updates an existing code-flow branch with new changes. Returns true if there were updates to push.
    /// </summary>
    private async Task<bool> UpdateAssetsAndSources(SubscriptionUpdateWorkItem update, InProgressPullRequest pullRequest)
    {
        var subscription = await _barClient.GetSubscriptionAsync(update.SubscriptionId)
                        ?? throw new Exception($"Subscription {update.SubscriptionId} not found");
        var build = await _barClient.GetBuildAsync(update.BuildId)
            ?? throw new Exception($"Build {update.BuildId} not found");

        var isForwardFlow = subscription.TargetDirectory != null;

        _logger.LogInformation(
            "{direction}-flowing build {buildId} for subscription {subscriptionId} targeting {repo} / {targetBranch} to new branch {newBranch}",
            isForwardFlow ? "Forward" : "Back",
            update.BuildId,
            subscription.Id,
            subscription.TargetRepository,
            subscription.TargetBranch,
            pullRequest.HeadBranch);

        bool hadUpdates;
        NativePath targetRepo;

        try
        {
            if (isForwardFlow)
            {
                hadUpdates = await _vmrForwardFlower.FlowForwardAsync(
                    subscription,
                    build,
                    pullRequest.HeadBranch,
                    cancellationToken: default);
                targetRepo = _vmrInfo.VmrPath;
            }
            else
            {
                (hadUpdates, targetRepo) = await _vmrBackFlower.FlowBackAsync(
                    subscription,
                    build,
                    pullRequest.HeadBranch,
                    cancellationToken: default);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to flow changes for build {buildId} in subscription {subscriptionId}",
                update.BuildId,
                subscription.Id);
            throw;
        }

        if (hadUpdates)
        {
            _logger.LogInformation("Code changes for {subscriptionId} ready in local branch {branch}",
                subscription.Id,
                pullRequest.HeadBranch);

            // TODO https://github.com/dotnet/arcade-services/issues/4199: Handle failures (conflict, non-ff etc)
            using (var scope = _telemetryRecorder.RecordGitOperation(TrackedGitOperation.Push, subscription.TargetRepository))
            {
                await _gitClient.Push(targetRepo, pullRequest.HeadBranch, subscription.TargetRepository);
                scope.SetSuccess();
            }
        }
        else
        {
            _logger.LogInformation("There were no code-flow updates for subscription {subscriptionId}",
                subscription.Id);
        }

        pullRequest.SourceSha = update.SourceSha;

        // TODO (https://github.com/dotnet/arcade-services/issues/3866): We need to update the InProgressPullRequest fully, assets and other info just like we do in UpdatePullRequestAsync
        // Right now, we are not flowing packages in codeflow subscriptions yet, so this functionality is no there
        // For now, we manually update the info the unit tests expect
        pullRequest.ContainedSubscriptions.Clear();
        pullRequest.ContainedSubscriptions =
        [
            new SubscriptionPullRequestUpdate
            {
                SubscriptionId = update.SubscriptionId,
                BuildId = update.BuildId
            }
        ];

        // Update PR's metadata
        var title = await _pullRequestBuilder.GenerateCodeFlowPRTitleAsync(update, subscription.TargetBranch);
        var description = await _pullRequestBuilder.GenerateCodeFlowPRDescriptionAsync(update);

        var remote = await _remoteFactory.CreateRemoteAsync(subscription.TargetRepository);
        await remote.UpdatePullRequestAsync(pullRequest.Url, new PullRequest
        {
            Title = title,
            Description = description
        });

        pullRequest.LastUpdate = DateTime.UtcNow;
        await SetPullRequestCheckReminder(pullRequest, true);
        await _pullRequestUpdateReminders.UnsetReminderAsync(true);

        return true;
    }

    /// <summary>
    /// Creates the code flow branch for the given subscription update.
    /// </summary>
    private async Task<string?> CreateCodeFlowBranchAsync(SubscriptionUpdateWorkItem update, string targetBranch)
    {
        string newBranchName = GetNewBranchName(targetBranch);

        _logger.LogInformation(
            "New code flow request for subscription {subscriptionId} / branch {branchName}",
            update.SubscriptionId,
            newBranchName);

        var subscription = await _barClient.GetSubscriptionAsync(update.SubscriptionId)
            ?? throw new Exception($"Subscription {update.SubscriptionId} not found");

        if (!subscription.SourceEnabled || (subscription.SourceDirectory ?? subscription.TargetDirectory) == null)
        {
            throw new Exception($"Subscription {update.SubscriptionId} is not source enabled or source directory is not set");
        }

        var build = await _barClient.GetBuildAsync(update.BuildId)
            ?? throw new Exception($"Build {update.BuildId} not found");
        var isForwardFlow = subscription.TargetDirectory != null;

        _logger.LogInformation(
            "{direction}-flowing build {buildId} for subscription {subscriptionId} targeting {repo} / {targetBranch} to new branch {newBranch}",
            isForwardFlow ? "Forward" : "Back",
            build.Id,
            subscription.Id,
            subscription.TargetRepository,
            subscription.TargetBranch,
            newBranchName);

        bool hadUpdates;
        NativePath targetRepo;

        try
        {
            if (isForwardFlow)
            {
                hadUpdates = await _vmrForwardFlower.FlowForwardAsync(
                    subscription,
                    build,
                    newBranchName,
                    cancellationToken: default);
                targetRepo = _vmrInfo.VmrPath;
            }
            else
            {
                (hadUpdates, targetRepo) = await _vmrBackFlower.FlowBackAsync(
                    subscription,
                    build,
                    newBranchName,
                    cancellationToken: default);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to flow changes for build {buildId} in subscription {subscriptionId}",
                build.Id,
                subscription.Id);
            throw;
        }

        if (!hadUpdates)
        {
            _logger.LogInformation("There were no code-flow updates for subscription {subscriptionId}",
                subscription.Id);
            return null;
        }

        _logger.LogInformation("Code changes for {subscriptionId} ready in local branch {branch}",
            subscription.Id,
            newBranchName);

        // TODO https://github.com/dotnet/arcade-services/issues/4199: Handle failures (conflict, non-ff etc)
        using (var scope = _telemetryRecorder.RecordGitOperation(TrackedGitOperation.Push, subscription.TargetRepository))
        {
            await _gitClient.Push(targetRepo, newBranchName, subscription.TargetRepository);
            scope.SetSuccess();
        }

        _logger.LogInformation("Code-flow branch {prBranch} pushed", newBranchName);
        return newBranchName;
    }

    private async Task CreateCodeFlowPullRequestAsync(
        SubscriptionUpdateWorkItem update,
        string targetRepository,
        string prBranch,
        string targetBranch)
    {
        bool isCodeFlow = update.SubscriptionType == SubscriptionType.DependenciesAndSources;
        IRemote darcRemote = await _remoteFactory.CreateRemoteAsync(targetRepository);

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

            // TODO (https://github.com/dotnet/arcade-services/issues/3866): Populate fully (assets, coherency checks..)
            InProgressPullRequest inProgressPr = new()
            {
                UpdaterId = Id.ToString(),
                Url = prUrl,
                HeadBranch = prBranch,
                SourceSha = update.SourceSha,
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

            inProgressPr.LastUpdate = DateTime.UtcNow;
            await SetPullRequestCheckReminder(inProgressPr, isCodeFlow);
            await _pullRequestUpdateReminders.UnsetReminderAsync(isCodeFlow);

            _logger.LogInformation("Code flow pull request created: {prUrl}", prUrl);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to create code flow pull request for subscription {subscriptionId}",
                update.SubscriptionId);
            await darcRemote.DeleteBranchAsync(targetRepository, prBranch);
            throw;
        }
    }

    #endregion
}
