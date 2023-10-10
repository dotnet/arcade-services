// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using Maestro.Contracts;
using Maestro.Data;
using Maestro.Data.Models;
using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.DotNet.ServiceFabric.ServiceHost.Actors;
using Microsoft.Extensions.Logging;
using Microsoft.ServiceFabric.Actors;
using Microsoft.ServiceFabric.Actors.Runtime;
using Microsoft.ServiceFabric.Data;
using Asset = Maestro.Contracts.Asset;
using AssetData = Microsoft.DotNet.Maestro.Client.Models.AssetData;

namespace SubscriptionActorService
{
    namespace unused
    {
        // class needed to appease service fabric build time generation of actor code
        [StatePersistence(StatePersistence.Persisted)]
        public class PullRequestActor : Actor, IPullRequestActor, IRemindable
        {
            public PullRequestActor(ActorService actorService, ActorId actorId) : base(actorService, actorId)
            {
            }

            public Task<string> RunActionAsync(string method, string arguments)
            {
                throw new NotImplementedException();
            }

            public Task UpdateAssetsAsync(Guid subscriptionId, int buildId, string sourceRepo, string sourceSha, List<Asset> assets)
            {
                throw new NotImplementedException();
            }

            public Task ReceiveReminderAsync(string reminderName, byte[] state, TimeSpan dueTime, TimeSpan period)
            {
                throw new NotImplementedException();
            }
        }
    }

    /// <summary>
    ///     A service fabric actor implementation that is responsible for creating and updating pull requests for dependency
    ///     updates.
    /// </summary>
    public class PullRequestActor : IPullRequestActor, IRemindable, IActionTracker, IActorImplementation
    {
        private readonly IMergePolicyEvaluator _mergePolicyEvaluator;
        private readonly BuildAssetRegistryContext _context;
        private readonly IRemoteFactory _darcFactory;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IActionRunner _actionRunner;
        private readonly IActorProxyFactory<ISubscriptionActor> _subscriptionActorFactory;
        private readonly IPullRequestPolicyFailureNotifier _pullRequestPolicyFailureNotifier;

        /// <summary>
        ///     Creates a new PullRequestActor
        /// </summary>
        /// <param name="id">
        ///     The actor id for this actor.
        ///     If it is a <see cref="Guid" /> actor id, then it is required to be the id of a non-batched subscription in the
        ///     database
        ///     If it is a <see cref="string" /> actor id, then it MUST be an actor id created with
        ///     <see cref="PullRequestActorId.Create(string, string)" /> for use with all subscriptions targeting the specified
        ///     repository and branch.
        /// </param>
        /// <param name="provider"></param>
        public PullRequestActor(
            IMergePolicyEvaluator mergePolicyEvaluator,
            BuildAssetRegistryContext context,
            IRemoteFactory darcFactory,
            ILoggerFactory loggerFactory,
            IActionRunner actionRunner,
            IActorProxyFactory<ISubscriptionActor> subscriptionActorFactory,
            IPullRequestPolicyFailureNotifier pullRequestPolicyFailureNotifier)
        {
            _mergePolicyEvaluator = mergePolicyEvaluator;
            _context = context;
            _darcFactory = darcFactory;
            _loggerFactory = loggerFactory;
            _actionRunner = actionRunner;
            _subscriptionActorFactory = subscriptionActorFactory;
            _pullRequestPolicyFailureNotifier = pullRequestPolicyFailureNotifier;
        }

        public void Initialize(ActorId actorId, IActorStateManager stateManager, IReminderManager reminderManager)
        {
            Implementation = GetImplementation(actorId, stateManager, reminderManager);
        }

        private PullRequestActorImplementation GetImplementation(ActorId actorId, IActorStateManager stateManager, IReminderManager reminderManager)
        {
            switch (actorId.Kind)
            {
                case ActorIdKind.Guid:
                    return new NonBatchedPullRequestActorImplementation(actorId,
                        reminderManager,
                        stateManager,
                        _mergePolicyEvaluator,
                        _context,
                        _darcFactory,
                        _loggerFactory,
                        _actionRunner,
                        _subscriptionActorFactory,
                        _pullRequestPolicyFailureNotifier);
                case ActorIdKind.String:
                    return new BatchedPullRequestActorImplementation(actorId,
                        reminderManager,
                        stateManager,
                        _mergePolicyEvaluator,
                        _context,
                        _darcFactory,
                        _loggerFactory,
                        _actionRunner,
                        _subscriptionActorFactory);
                default:
                    throw new NotSupportedException("Only actorIds of type Guid and String are supported");
            }
        }

        public PullRequestActorImplementation Implementation { get; private set; }

        public Task TrackSuccessfulAction(string action, string result)
        {
            return Implementation.TrackSuccessfulAction(action, result);
        }

        public Task TrackFailedAction(string action, string result, string method, string arguments)
        {
            return Implementation.TrackFailedAction(action, result, method, arguments);
        }

        public Task<string> RunActionAsync(string method, string arguments)
        {
            return Implementation.RunActionAsync(method, arguments);
        }

        public Task UpdateAssetsAsync(Guid subscriptionId, int buildId, string sourceRepo, string sourceSha, List<Asset> assets)
        {
            return Implementation.UpdateAssetsAsync(subscriptionId, buildId, sourceRepo, sourceSha, assets);
        }

        public async Task ReceiveReminderAsync(string reminderName, byte[] state, TimeSpan dueTime, TimeSpan period)
        {
            if (reminderName == PullRequestActorImplementation.PullRequestCheck)
            {
                await Implementation.SynchronizeInProgressPullRequestAsync();
            }
            else if (reminderName == PullRequestActorImplementation.PullRequestUpdate)
            {
                await Implementation.RunProcessPendingUpdatesAsync();
            }
            else
            {
                throw new ReminderNotFoundException(reminderName);
            }
        }
    }

    public abstract class PullRequestActorImplementation : IPullRequestActor, IActionTracker
    {
        public const string PullRequestCheck = "pullRequestCheck";
        public const string PullRequestUpdate = "pullRequestUpdate";
        public const string PullRequest = "pullRequest";
        public const string DependencyUpdateBegin = "[DependencyUpdate]: <> (Begin)";
        public const string DependencyUpdateEnd = "[DependencyUpdate]: <> (End)";

        protected PullRequestActorImplementation(
            ActorId id,
            IReminderManager reminders,
            IActorStateManager stateManager,
            IMergePolicyEvaluator mergePolicyEvaluator,
            BuildAssetRegistryContext context,
            IRemoteFactory darcFactory,
            ILoggerFactory loggerFactory,
            IActionRunner actionRunner,
            IActorProxyFactory<ISubscriptionActor> subscriptionActorFactory)
        {
            Id = id;
            Reminders = reminders;
            StateManager = stateManager;
            MergePolicyEvaluator = mergePolicyEvaluator;
            Context = context;
            DarcRemoteFactory = darcFactory;
            ActionRunner = actionRunner;
            SubscriptionActorFactory = subscriptionActorFactory;
            LoggerFactory = loggerFactory;
            Logger = loggerFactory.CreateLogger(GetType());
        }

        public ILogger Logger { get; }
        public ILoggerFactory LoggerFactory { get; }
        public ActorId Id { get; }
        public IReminderManager Reminders { get; }
        public IActorStateManager StateManager { get; }
        public IMergePolicyEvaluator MergePolicyEvaluator { get; }
        public BuildAssetRegistryContext Context { get; }
        public IRemoteFactory DarcRemoteFactory { get; }
        public IActionRunner ActionRunner { get; }
        public IActorProxyFactory<ISubscriptionActor> SubscriptionActorFactory { get; }

        public async Task TrackSuccessfulAction(string action, string result)
        {
            RepositoryBranchUpdate update = await GetRepositoryBranchUpdate();

            update.Action = action;
            update.ErrorMessage = result;
            update.Method = null;
            update.Arguments = null;
            update.Success = true;
            await Context.SaveChangesAsync();
        }

        public async Task TrackFailedAction(string action, string result, string method, string arguments)
        {
            RepositoryBranchUpdate update = await GetRepositoryBranchUpdate();

            update.Action = action;
            update.ErrorMessage = result;
            update.Method = method;
            update.Arguments = arguments;
            update.Success = false;
            await Context.SaveChangesAsync();
        }

        public Task<string> RunActionAsync(string method, string arguments)
        {
            return ActionRunner.RunAction(this, method, arguments);
        }

        Task IPullRequestActor.UpdateAssetsAsync(Guid subscriptionId, int buildId, string sourceRepo, string sourceSha, List<Asset> assets)
        {
            return ActionRunner.ExecuteAction(() => UpdateAssetsAsync(subscriptionId, buildId, sourceRepo, sourceSha, assets));
        }

        protected abstract Task<(string repository, string branch)> GetTargetAsync();

        protected abstract Task<IReadOnlyList<MergePolicyDefinition>> GetMergePolicyDefinitions();

        private class ReferenceLinksMap
        {
            public Dictionary<(string from, string to), int> ShaRangeToLinkId { get; } = new Dictionary<(string from, string to), int>();
        }

        private async Task<string> GetSourceRepositoryAsync(Guid subscriptionId)
        {
            Subscription subscription = await Context.Subscriptions.FindAsync(subscriptionId);
            return subscription?.SourceRepository;
        }

        /// <summary>
        /// Retrieve the build from a database build id.
        /// </summary>
        /// <param name="buildId">Build id</param>
        /// <returns>Build</returns>
        private Task<Build> GetBuildAsync(int buildId)
        {
            return Context.Builds.FindAsync(buildId).AsTask();
        }

        public Task RunProcessPendingUpdatesAsync()
        {
            return ActionRunner.ExecuteAction(() => ProcessPendingUpdatesAsync());
        }

        /// <summary>
        ///     Process any pending pull request updates stored in the <see cref="PullRequestUpdate" />
        ///     actor state key.
        /// </summary>
        /// <returns>
        ///     An <see cref="ActionResult{bool}" /> containing:
        ///     <see langword="true" /> if updates have been applied; <see langword="false" /> otherwise.
        /// </returns>
        [ActionMethod("Processing pending updates")]
        public async Task<ActionResult<bool>> ProcessPendingUpdatesAsync()
        {
            Logger.LogInformation("Processing pending updates");
            ConditionalValue<List<UpdateAssetsParameters>> maybeUpdates =
                await StateManager.TryGetStateAsync<List<UpdateAssetsParameters>>(PullRequestUpdate);
            List<UpdateAssetsParameters> updates = maybeUpdates.HasValue ? maybeUpdates.Value : null;
            if (updates == null || updates.Count < 1)
            {
                Logger.LogInformation("No Pending Updates");
                await Reminders.TryUnregisterReminderAsync(PullRequestUpdate);
                return ActionResult.Create(false, "No Pending Updates");
            }

            (InProgressPullRequest pr, bool canUpdate) = await SynchronizeInProgressPullRequestAsync();

            if (pr != null && !canUpdate)
            {
                Logger.LogInformation($"PR {pr?.Url} cannot be updated.");
                return ActionResult.Create(false, "PR cannot be updated.");
            }

            string result;
            if (pr != null)
            {
                await UpdatePullRequestAsync(pr, updates);
                result = $"Pull Request '{pr.Url}' updated.";
                Logger.LogInformation($"Pull Request '{pr.Url}' updated.");
            }
            else
            {
                string prUrl = await CreatePullRequestAsync(updates);
                if (prUrl == null)
                {
                    result = "No changes required, no pull request created.";
                }
                else
                {
                    result = $"Pull Request '{prUrl}' created.";
                }
                Logger.LogInformation(result);
            }

            await StateManager.RemoveStateAsync(PullRequestUpdate);
            await Reminders.TryUnregisterReminderAsync(PullRequestUpdate);

            return ActionResult.Create(true, "Pending updates applied. " + result);
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
        public virtual async Task<(InProgressPullRequest pr, bool canUpdate)> SynchronizeInProgressPullRequestAsync()
        {
            ConditionalValue<InProgressPullRequest> maybePr =
                await StateManager.TryGetStateAsync<InProgressPullRequest>(PullRequest);
            if (maybePr.HasValue)
            {
                InProgressPullRequest pr = maybePr.Value;
                if (string.IsNullOrEmpty(pr.Url))
                {
                    // somehow a bad PR got in the collection, remove it
                    await StateManager.RemoveStateAsync(PullRequest);
                    return (null, false);
                }

                SynchronizePullRequestResult result = await ActionRunner.ExecuteAction(() => SynchronizePullRequestAsync(pr.Url));

                switch (result)
                {
                    // If the PR was merged or closed, we are done with it and the actor doesn't
                    // need to periodically run the synchronization any longer.
                    case SynchronizePullRequestResult.Completed:
                    case SynchronizePullRequestResult.UnknownPR:
                        await Reminders.TryUnregisterReminderAsync(PullRequestCheck);
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
                        Logger.LogError($"Unknown pull request synchronization result {result}");
                        break;
                }
            }

            await Reminders.TryUnregisterReminderAsync(PullRequestCheck);
            return (null, false);
        }

        /// <summary>
        ///     Synchronizes a pull request
        /// </summary>
        /// <param name="prUrl">Pull request url.</param>
        /// <returns>
        ///    Result of the synchronization
        /// </returns>
        [ActionMethod("Synchronizing Pull Request: '{url}'")]
        private async Task<ActionResult<SynchronizePullRequestResult>> SynchronizePullRequestAsync(string prUrl)
        {
            Logger.LogInformation($"Synchronizing Pull Request {prUrl}");
            ConditionalValue<InProgressPullRequest> maybePr =
                await StateManager.TryGetStateAsync<InProgressPullRequest>(PullRequest);
            if (!maybePr.HasValue || maybePr.Value.Url != prUrl)
            {
                Logger.LogInformation($"Not Applicable: Pull Request '{prUrl}' is not tracked by maestro anymore.");
                return ActionResult.Create(
                    SynchronizePullRequestResult.UnknownPR,
                    $"Not Applicable: Pull Request '{prUrl}' is not tracked by maestro anymore.");
            }

            (string targetRepository, _) = await GetTargetAsync();
            IRemote darc = await DarcRemoteFactory.GetRemoteAsync(targetRepository, Logger);

            InProgressPullRequest pr = maybePr.Value;

            Logger.LogInformation($"Get status for PullRequest: {prUrl}");
            PrStatus status = await darc.GetPullRequestStatusAsync(prUrl);
            switch (status)
            {
                // If the PR is currently open, then evaluate the merge policies, which will potentially
                // merge the PR if they are successful.
                case PrStatus.Open:
                    Logger.LogInformation($"Status open for: {prUrl}");
                    ActionResult<MergePolicyCheckResult> checkPolicyResult = await CheckMergePolicyAsync(pr, darc);
                    pr.MergePolicyResult = checkPolicyResult.Result;

                    switch (checkPolicyResult.Result)
                    {
                        case MergePolicyCheckResult.Merged:
                            await UpdateSubscriptionsForMergedPRAsync(pr.ContainedSubscriptions);
                            await AddDependencyFlowEventsAsync(
                                pr.ContainedSubscriptions,
                                DependencyFlowEventType.Completed,
                                DependencyFlowEventReason.AutomaticallyMerged,
                                checkPolicyResult.Result,
                                prUrl);
                            await StateManager.RemoveStateAsync(PullRequest);
                            return ActionResult.Create(SynchronizePullRequestResult.Completed, checkPolicyResult.Message);
                        case MergePolicyCheckResult.FailedPolicies:
                            await TagSourceRepositoryGitHubContactsIfPossibleAsync(pr);
                            goto case MergePolicyCheckResult.FailedToMerge;
                        case MergePolicyCheckResult.NoPolicies:
                        case MergePolicyCheckResult.FailedToMerge:
                            return ActionResult.Create(SynchronizePullRequestResult.InProgressCanUpdate, checkPolicyResult.Message);
                        case MergePolicyCheckResult.PendingPolicies:
                            return ActionResult.Create(SynchronizePullRequestResult.InProgressCannotUpdate, checkPolicyResult.Message);
                        default:
                            throw new NotImplementedException($"Unknown merge policy check result {checkPolicyResult.Result}");
                    }
                case PrStatus.Merged:
                case PrStatus.Closed:
                    // If the PR has been merged, update the subscription information
                    Logger.LogInformation($"Status closed for: {prUrl}");
                    if (status == PrStatus.Merged)
                    {
                        await UpdateSubscriptionsForMergedPRAsync(pr.ContainedSubscriptions);
                    }

                    DependencyFlowEventReason reason = status == PrStatus.Merged ?
                        DependencyFlowEventReason.ManuallyMerged :
                        DependencyFlowEventReason.ManuallyClosed;

                    await AddDependencyFlowEventsAsync(
                        pr.ContainedSubscriptions,
                        DependencyFlowEventType.Completed,
                        reason,
                        pr.MergePolicyResult,
                        prUrl);
                    await StateManager.RemoveStateAsync(PullRequest);

                    // Also try to clean up the PR branch.
                    try
                    {
                        Logger.LogInformation($"Try to clean up the PR branch {prUrl}");
                        await darc.DeletePullRequestBranchAsync(prUrl);
                    }
                    catch (DarcException e)
                    {
                        Logger.LogInformation(e, $"Failed to delete Branch associated with pull request {prUrl}");
                    }
                    return ActionResult.Create(SynchronizePullRequestResult.Completed, $"PR Has been manually {status}");
                default:
                    throw new NotImplementedException($"Unknown pr status '{status}'");
            }
        }

        /// <summary>
        ///     Check the merge policies for a PR and merge if they have succeeded.
        /// </summary>
        /// <param name="prUrl">Pull request URL</param>
        /// <param name="darc">Darc remote</param>
        /// <returns>Result of the policy check.</returns>
        private async Task<ActionResult<MergePolicyCheckResult>> CheckMergePolicyAsync(IPullRequest pr, IRemote darc)
        {
            IReadOnlyList<MergePolicyDefinition> policyDefinitions = await GetMergePolicyDefinitions();
            MergePolicyEvaluationResults result = await MergePolicyEvaluator.EvaluateAsync(
                pr,
                darc,
                policyDefinitions);

            await UpdateMergeStatusAsync(darc, pr.Url, result.Results);
            // As soon as one policy is actively failed, we enter a failed state.
            if (result.Failed)
            {
                Logger.LogInformation($"NOT Merged: PR '{pr.Url}' failed policies {string.Join(", ", result.Results.Where(r => r.Status != MergePolicyEvaluationStatus.Success).Select(r => r.MergePolicyInfo.Name + r.Title))}");
                return ActionResult.Create(MergePolicyCheckResult.FailedPolicies,
                    $"NOT Merged: PR '{pr.Url}' has failed policies {string.Join(", ", result.Results.Where(r => r.Status == MergePolicyEvaluationStatus.Failure).Select(r => r.MergePolicyInfo.Name + r.Title))}");
            }
            else if (result.Pending)
            {
                return ActionResult.Create(MergePolicyCheckResult.PendingPolicies,
                    $"NOT Merged: PR '{pr.Url}' has pending policies {string.Join(", ", result.Results.Where(r => r.Status == MergePolicyEvaluationStatus.Pending).Select(r => r.MergePolicyInfo.Name + r.Title))}");
            }
            if (result.Succeeded)
            {
                var merged = false;
                try
                {
                    await darc.MergeDependencyPullRequestAsync(pr.Url, new MergePullRequestParameters());
                    merged = true;
                }
                catch
                {
                    Logger.LogInformation($"NOT Merged: PR '{pr.Url}' Failed to merge");
                    // Failure to merge is not exceptional, report on it.
                }
                if (merged)
                {
                    string passedPolicies = string.Join(", ", policyDefinitions.Select(p => p.Name));
                    Logger.LogInformation($"Merged: PR '{pr.Url}' passed policies {passedPolicies}");
                    return ActionResult.Create(
                        MergePolicyCheckResult.Merged,
                        $"Merged: PR '{pr.Url}' passed policies {passedPolicies}");
                }
                Logger.LogInformation($"NOT Merged: PR '{pr.Url}' has merge conflicts.");
                return ActionResult.Create(MergePolicyCheckResult.FailedToMerge, $"NOT Merged: PR '{pr.Url}' has merge conflicts.");
            }
            Logger.LogInformation($"NOT Merged: PR '{pr.Url}' There are no merge policies");
            return ActionResult.Create(MergePolicyCheckResult.NoPolicies, "NOT Merged: There are no merge policies");
        }

        /// <summary>
        ///     Create new checks or update the status of existing checks for a PR.
        /// </summary>
        /// <param name="prUrl">Pull request URL</param>
        /// <param name="darc">Darc remote</param>
        /// <param name="evaluations">List of merge policies</param>
        /// <returns>Result of the policy check.</returns>
        private Task UpdateMergeStatusAsync(IRemote darc, string prUrl, IReadOnlyList<MergePolicyEvaluationResult> evaluations)
        {
            return darc.CreateOrUpdatePullRequestMergeStatusInfoAsync(prUrl, evaluations);
        }

        private async Task UpdateSubscriptionsForMergedPRAsync(
            IEnumerable<SubscriptionPullRequestUpdate> subscriptionPullRequestUpdates)
        {
            Logger.LogInformation("Updating subscriptions for merged PR");
            foreach (SubscriptionPullRequestUpdate update in subscriptionPullRequestUpdates)
            {
                ISubscriptionActor actor = SubscriptionActorFactory.Lookup(new ActorId(update.SubscriptionId));
                if (!await actor.UpdateForMergedPullRequestAsync(update.BuildId))
                {
                    Logger.LogInformation($"Failed to update subscription {update.SubscriptionId} for merged PR.");
                    await Reminders.TryUnregisterReminderAsync(PullRequestCheck);
                    await Reminders.TryUnregisterReminderAsync(PullRequestUpdate);
                    await StateManager.TryRemoveStateAsync(PullRequest);
                }
            }
        }

        private async Task AddDependencyFlowEventsAsync(
            IEnumerable<SubscriptionPullRequestUpdate> subscriptionPullRequestUpdates,
            DependencyFlowEventType flowEvent,
            DependencyFlowEventReason reason,
            MergePolicyCheckResult policy,
            string prUrl)
        {

            foreach (SubscriptionPullRequestUpdate update in subscriptionPullRequestUpdates)
            {
                ISubscriptionActor actor = SubscriptionActorFactory.Lookup(new ActorId(update.SubscriptionId));
                if (!await actor.AddDependencyFlowEventAsync(update.BuildId, flowEvent, reason, policy, "PR", prUrl))
                {
                    Logger.LogInformation($"Failed to add dependency flow event for {update.SubscriptionId}.");
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
        /// <returns></returns>
        [ActionMethod("Updating assets for subscription: {subscriptionId}, build: {buildId}")]
        public async Task<ActionResult<object>> UpdateAssetsAsync(
            Guid subscriptionId,
            int buildId,
            string sourceRepo,
            string sourceSha,
            List<Asset> assets)
        {
            (InProgressPullRequest pr, bool canUpdate) = await SynchronizeInProgressPullRequestAsync();

            var updateParameter = new UpdateAssetsParameters
            {
                SubscriptionId = subscriptionId,
                BuildId = buildId,
                SourceSha = sourceSha,
                SourceRepo = sourceRepo,
                Assets = assets,
                IsCoherencyUpdate = false
            };

            try
            {
                if (pr != null && !canUpdate)
                {
                    await StateManager.AddOrUpdateStateAsync(
                        PullRequestUpdate,
                        new List<UpdateAssetsParameters> { updateParameter },
                        (n, old) =>
                        {
                            old.Add(updateParameter);
                            return old;
                        });
                    await Reminders.TryRegisterReminderAsync(
                        PullRequestUpdate,
                        Array.Empty<byte>(),
                        TimeSpan.FromMinutes(5),
                        TimeSpan.FromMinutes(5));
                    return ActionResult.Create<object>(
                        null,
                        $"Current Pull request '{pr.Url}' cannot be updated, update queued.");
                }

                if (pr != null)
                {
                    await UpdatePullRequestAsync(pr, new List<UpdateAssetsParameters> { updateParameter });
                    return ActionResult.Create<object>(null, $"Pull Request '{pr.Url}' updated.");
                }

                string prUrl = await CreatePullRequestAsync(new List<UpdateAssetsParameters> { updateParameter });
                if (prUrl == null)
                {
                    return ActionResult.Create<object>(null, "Updates require no changes, no pull request created.");
                }

                return ActionResult.Create<object>(null, $"Pull request '{prUrl}' created.");
            }
            catch (HttpRequestException reqEx) when (reqEx.Message.Contains(((int) HttpStatusCode.Unauthorized).ToString()))
            {
                // We want to preserve the HttpRequestException's information but it's not serializable
                // We'll log the full exception object so it's in Application Insights, and strip any single quotes from the message to ensure 
                // GitHub issues are properly created.
                Logger.LogError(reqEx, "Failure to authenticate to repository");
                throw new DarcAuthenticationFailureException($"Failure to authenticate: {reqEx.Message}");
            }
        }

        /// <summary>
        ///     Compute the title for a pull request.
        /// </summary>
        /// <param name="inProgressPr">Current in progress pull request information</param>
        /// <returns>Pull request title</returns>
        private async Task<string> ComputePullRequestTitleAsync(InProgressPullRequest inProgressPr, string targetBranch)
        {
            // Get the unique subscription IDs. It may be possible for a coherency update
            // to not have any contained subscription.  In this case
            // we return a different title.
            var uniqueSubscriptionIds = inProgressPr.ContainedSubscriptions.Select(
                subscription => subscription.SubscriptionId).ToHashSet();

            if (uniqueSubscriptionIds.Count > 0)
            {
                // We'll either list out the repos involved (in a shortened form)
                // or we'll list out the number of repos that are involved.
                // Start building up the list. If we reach a max length, then backtrack and
                // just note the number of input subscriptions.
                string baseTitle = $"[{targetBranch}] Update dependencies from";
                StringBuilder titleBuilder = new StringBuilder(baseTitle);
                bool prefixComma = false;
                // Github title limit -348 
                // Azdo title limit - 419 
                // maxTitleLength = 150 to fit 2/3 repo names in the title. 
                const int maxTitleLength = 150;
                foreach (Guid subscriptionId in uniqueSubscriptionIds)
                {
                    string repoName = await GetSourceRepositoryAsync(subscriptionId);

                    // Strip down repo name.
                    repoName = repoName?.Replace("https://github.com/", "");
                    repoName = repoName?.Replace("https://dev.azure.com/", "");
                    repoName = repoName?.Replace("_git/", "");
                    string repoNameForTitle = prefixComma ? $", {repoName}" : repoName;

                    if (titleBuilder.Length + repoNameForTitle?.Length > maxTitleLength)
                    {
                        return $"{baseTitle} {uniqueSubscriptionIds.Count} repositories";
                    }
                    else
                    {
                        titleBuilder.Append(" " + repoNameForTitle);
                    }
                }

                return titleBuilder.ToString();
            }
            else
            {
                return $"[{targetBranch}] Update dependencies to ensure coherency";
            }
        }

        /// <summary>
        ///     Creates a pull request from the given updates.
        /// </summary>
        /// <param name="updates"></param>
        /// <returns>The pull request url when a pr was created; <see langref="null" /> if no PR is necessary</returns>
        private async Task<string> CreatePullRequestAsync(List<UpdateAssetsParameters> updates)
        {
            (string targetRepository, string targetBranch) = await GetTargetAsync();
            IRemote darcRemote = await DarcRemoteFactory.GetRemoteAsync(targetRepository, Logger);

            TargetRepoDependencyUpdate repoDependencyUpdate =
                await GetRequiredUpdates(updates, DarcRemoteFactory, targetRepository, targetBranch);

            if (repoDependencyUpdate.CoherencyCheckSuccessful && repoDependencyUpdate.RequiredUpdates.Count() < 1)
            {
                return null;
            }

            string newBranchName = $"darc-{targetBranch}-{Guid.NewGuid()}";
            await darcRemote.CreateNewBranchAsync(targetRepository, targetBranch, newBranchName);

            try
            {
                string description = await CalculatePRDescriptionAndCommitUpdatesAsync(
                    repoDependencyUpdate.RequiredUpdates,
                    null,
                    DarcRemoteFactory,
                    targetRepository,
                    newBranchName);

                var inProgressPr = new InProgressPullRequest {
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

                string prUrl = await darcRemote.CreatePullRequestAsync(
                    targetRepository,
                    new PullRequest
                    {
                        Title = await ComputePullRequestTitleAsync(inProgressPr, targetBranch),
                        Description = description,
                        BaseBranch = targetBranch,
                        HeadBranch = newBranchName
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

                    await StateManager.SetStateAsync(PullRequest, inProgressPr);
                    await StateManager.SaveStateAsync();
                    await Reminders.TryRegisterReminderAsync(
                        PullRequestCheck,
                        null,
                        TimeSpan.FromMinutes(5),
                        TimeSpan.FromMinutes(5));
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

        /// <summary>
        /// Commit a dependency update to a target branch and calculate the PR description
        /// </summary>
        /// <param name="requiredUpdates">Version updates to apply</param>
        /// <param name="description">
        ///     A string writer that the PR description should be written to. If this an update
        ///     to an existing PR, this will contain the existing PR description.
        /// </param>
        /// <param name="remoteFactory">Remote factory for generating remotes based on repo uri</param>
        /// <param name="targetRepository">Target repository that the updates should be applied to</param>
        /// <param name="newBranchName">Target branch the updates should be to</param>
        /// <returns></returns>
        private async Task<string> CalculatePRDescriptionAndCommitUpdatesAsync(
            List<(UpdateAssetsParameters update, List<DependencyUpdate> deps)> requiredUpdates,
            string description,
            IRemoteFactory remoteFactory,
            string targetRepository,
            string newBranchName)
        {
            // First run through non-coherency and then do a coherency
            // message if one exists.
            List<(UpdateAssetsParameters update, List<DependencyUpdate> deps)> nonCoherencyUpdates =
                requiredUpdates.Where(u => !u.update.IsCoherencyUpdate).ToList();
            // Should max one coherency update
            (UpdateAssetsParameters update, List<DependencyUpdate> deps) coherencyUpdate =
                requiredUpdates.Where(u => u.update.IsCoherencyUpdate).SingleOrDefault();

            IRemote remote = await remoteFactory.GetRemoteAsync(targetRepository, Logger);

            // To keep a PR to as few commits as possible, if the number of
            // non-coherency updates is 1 then combine coherency updates with those.
            // Otherwise, put all coherency updates in a separate commit.
            bool combineCoherencyWithNonCoherency = (nonCoherencyUpdates.Count == 1);
            PullRequestDescriptionBuilder pullRequestDescriptionBuilder = new PullRequestDescriptionBuilder(LoggerFactory, description);

            foreach ((UpdateAssetsParameters update, List<DependencyUpdate> deps) in nonCoherencyUpdates)
            {
                var message = new StringBuilder();
                List<DependencyUpdate> dependenciesToCommit = deps;
                await CalculateCommitMessage(update, deps, message);
                Build build = await GetBuildAsync(update.BuildId);

                if (combineCoherencyWithNonCoherency && coherencyUpdate.update != null)
                {
                    await CalculateCommitMessage(coherencyUpdate.update, coherencyUpdate.deps, message);
                    pullRequestDescriptionBuilder.AppendBuildDescription(coherencyUpdate.update, coherencyUpdate.deps, null, build);
                    dependenciesToCommit.AddRange(coherencyUpdate.deps);
                }

                List<GitFile> committedFiles = await remote.CommitUpdatesAsync(targetRepository, newBranchName, remoteFactory,
                    dependenciesToCommit.Select(du => du.To).ToList(), message.ToString());
                pullRequestDescriptionBuilder.AppendBuildDescription(update, deps, committedFiles, build);
            }

            // If the coherency update wasn't combined, then
            // add it now
            if (!combineCoherencyWithNonCoherency && coherencyUpdate.update != null)
            {
                var message = new StringBuilder();
                Build build = await GetBuildAsync(coherencyUpdate.update.BuildId);
                await CalculateCommitMessage(coherencyUpdate.update, coherencyUpdate.deps, message);
                pullRequestDescriptionBuilder.AppendBuildDescription(coherencyUpdate.update, coherencyUpdate.deps, null, build);

                await remote.CommitUpdatesAsync(targetRepository, newBranchName, remoteFactory,
                    coherencyUpdate.deps.Select(du => du.To).ToList(), message.ToString());
            }

            // If the coherency algorithm failed and there are no non-coherency updates and
            // we crate an empty commit that describes an issue.
            if (requiredUpdates.Count == 0)
            {
                string message = "Failed to perform coherency update for one or more dependencies.";
                await remote.CommitUpdatesAsync(targetRepository, newBranchName, remoteFactory, new List<DependencyDetail>(), message);
                return $"Coherency update: {message} Please review the GitHub checks or run `darc update-dependencies --coherency-only` locally against {newBranchName} for more information.";
            }

            return pullRequestDescriptionBuilder.ToString();
        }

        public static void UpdatePRDescriptionDueConfigFiles(List<GitFile> committedFiles, StringBuilder globalJsonSection)
        {
            GitFile globalJsonFile = committedFiles?.
                Where(gf => gf.FilePath.Equals("global.json", StringComparison.OrdinalIgnoreCase)).
                FirstOrDefault();

            // The list of committedFiles can contain the `global.json` file (and others) 
            // even though no actual change was made to the file and therefore there is no 
            // metadata for it.
            if (globalJsonFile?.Metadata != null)
            {
                bool hasSdkVersionUpdate = globalJsonFile.Metadata.ContainsKey(GitFileMetadataName.SdkVersionUpdate);
                bool hasToolsDotnetUpdate = globalJsonFile.Metadata.ContainsKey(GitFileMetadataName.ToolsDotNetUpdate);

                globalJsonSection.AppendLine("- **Updates to .NET SDKs:**");

                if (hasSdkVersionUpdate)
                {
                    globalJsonSection.AppendLine($"  - Updates sdk.version to " +
                        $"{globalJsonFile.Metadata[GitFileMetadataName.SdkVersionUpdate]}");
                }

                if (hasToolsDotnetUpdate)
                {
                    globalJsonSection.AppendLine($"  - Updates tools.dotnet to " +
                        $"{globalJsonFile.Metadata[GitFileMetadataName.ToolsDotNetUpdate]}");
                }
            }
        }

        private async Task CalculateCommitMessage(UpdateAssetsParameters update, List<DependencyUpdate> deps, StringBuilder message)
        {
            if (update.IsCoherencyUpdate)
            {
                message.AppendLine("Dependency coherency updates");
                message.AppendLine();
                message.AppendLine(string.Join(",", deps.Select(p => p.To.Name)));
                message.AppendLine($" From Version {deps[0].From.Version} -> To Version {deps[0].To.Version} (parent: {deps[0].To.CoherentParentDependencyName}");
            }
            else
            {
                string sourceRepository = update.SourceRepo;
                Build build = await GetBuildAsync(update.BuildId);
                message.AppendLine($"Update dependencies from {sourceRepository} build {build.AzureDevOpsBuildNumber}");
                message.AppendLine();
                message.AppendLine(string.Join(" , ", deps.Select(p => p.To.Name)));
                message.AppendLine($" From Version {deps[0].From.Version} -> To Version {deps[0].To.Version}");
            }

            message.AppendLine();
        }

        private async Task UpdatePullRequestAsync(InProgressPullRequest pr, List<UpdateAssetsParameters> updates)
        {
            (string targetRepository, string targetBranch) = await GetTargetAsync();
            IRemote darcRemote = await DarcRemoteFactory.GetRemoteAsync(targetRepository, Logger);

            TargetRepoDependencyUpdate targetRepositoryUpdates =
                await GetRequiredUpdates(updates, DarcRemoteFactory, targetRepository, targetBranch);

            if (targetRepositoryUpdates.CoherencyCheckSuccessful && targetRepositoryUpdates.RequiredUpdates.Count() < 1)
            {
                return;
            }

            pr.RequiredUpdates = MergeExistingWithIncomingUpdates(pr.RequiredUpdates, targetRepositoryUpdates.RequiredUpdates);
            pr.CoherencyCheckSuccessful = targetRepositoryUpdates.CoherencyCheckSuccessful;
            pr.CoherencyErrors = targetRepositoryUpdates.CoherencyErrors;

            PullRequest pullRequest = await darcRemote.GetPullRequestAsync(pr.Url);
            string headBranch = pullRequest.HeadBranch;

            List<SubscriptionPullRequestUpdate> previousSubscriptions = new List<SubscriptionPullRequestUpdate>(pr.ContainedSubscriptions);

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

            pullRequest.Description = await CalculatePRDescriptionAndCommitUpdatesAsync(
                targetRepositoryUpdates.RequiredUpdates,
                pullRequest.Description,
                DarcRemoteFactory,
                targetRepository,
                headBranch);
            pullRequest.Title = await ComputePullRequestTitleAsync(pr, targetBranch);
            await darcRemote.UpdatePullRequestAsync(pr.Url, pullRequest);

            await StateManager.SetStateAsync(PullRequest, pr);
            await StateManager.SaveStateAsync();
            await Reminders.TryRegisterReminderAsync(
                PullRequestCheck,
                null,
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(5));
        }


        /// <summary>
        /// Merges the list of existing updates in a PR with a list of incoming updates
        /// </summary>
        /// <param name="existingUpdates">pr object to update</param>
        /// <param name="incomingUpdates">list of new incoming updates</param>
        /// <returns>Merged list of existing updates along with the new</returns>
        private List<DependencyUpdateSummary> MergeExistingWithIncomingUpdates(
            List<DependencyUpdateSummary> existingUpdates,
            List<(UpdateAssetsParameters update, List<DependencyUpdate> deps)> incomingUpdates)
        {
            // First project the new updates to the final list
            List<DependencyUpdateSummary> mergedUpdates =
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
            public List<CoherencyErrorDetails> CoherencyErrors { get; set; }
            public List<(UpdateAssetsParameters update, List<DependencyUpdate> deps)> RequiredUpdates { get; set; } = new();
        }

        /// <summary>
        /// Given a set of input updates from builds, determine what updates
        /// are required in the target repository.
        /// </summary>
        /// <param name="updates">Updates</param>
        /// <param name="targetRepository">Target repository to calculate updates for</param>
        /// <param name="branch">Target branch</param>
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
            string branch)
        {
            Logger.LogInformation($"Getting Required Updates from {branch} to {targetRepository}");
            // Get a remote factory for the target repo
            IRemote darc = await remoteFactory.GetRemoteAsync(targetRepository, Logger);

            TargetRepoDependencyUpdate repoDependencyUpdate = new();

            // Existing details 
            List<DependencyDetail> existingDependencies = (await darc.GetDependenciesAsync(targetRepository, branch)).ToList();

            foreach (UpdateAssetsParameters update in updates)
            {
                IEnumerable<AssetData> assetData = update.Assets.Select(
                    a => new AssetData(false)
                    {
                        Name = a.Name,
                        Version = a.Version
                    });
                // Retrieve the source of the assets

                List<DependencyUpdate> dependenciesToUpdate = await darc.GetRequiredNonCoherencyUpdatesAsync(
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
                            new SubscriptionPullRequestUpdate
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
            List<DependencyUpdate> coherencyUpdates = new List<DependencyUpdate>();
            try
            {
                Logger.LogInformation($"Running a coherency check on the existing dependencies for branch {branch} of repo {targetRepository}");
                coherencyUpdates = await darc.GetRequiredCoherencyUpdatesAsync(existingDependencies, remoteFactory);
            }
            catch (DarcCoherencyException e)
            {
                Logger.LogInformation("Failed attempting strict coherency update on branch '{strictCoherencyFailedBranch}' of repo '{strictCoherencyFailedRepo}'.",
                     branch, targetRepository);
                repoDependencyUpdate.CoherencyCheckSuccessful = false;
                repoDependencyUpdate.CoherencyErrors = e.Errors.Select(e => new CoherencyErrorDetails
                {
                    Error = e.Error,
                    PotentialSolutions = e.PotentialSolutions
                }).ToList();
            }

            if (coherencyUpdates.Any())
            {
                // For the update asset parameters, we don't have any information on the source of the update,
                // since coherency can be run even without any updates.
                UpdateAssetsParameters coherencyUpdateParameters = new UpdateAssetsParameters
                {
                    IsCoherencyUpdate = true
                };
                repoDependencyUpdate.RequiredUpdates.Add((coherencyUpdateParameters, coherencyUpdates.ToList()));
            }

            Logger.LogInformation("Finished getting Required Updates from {branch} to {targetRepository}", branch, targetRepository);
            return repoDependencyUpdate;
        }

        private async Task<RepositoryBranchUpdate> GetRepositoryBranchUpdate()
        {
            (string repo, string branch) = await GetTargetAsync();
            RepositoryBranchUpdate update = await Context.RepositoryBranchUpdates.FindAsync(repo, branch);
            if (update == null)
            {
                RepositoryBranch repoBranch = await GetRepositoryBranch(repo, branch);
                Context.RepositoryBranchUpdates.Add(
                    update = new RepositoryBranchUpdate { RepositoryBranch = repoBranch });
            }
            else
            {
                Context.RepositoryBranchUpdates.Update(update);
            }

            return update;
        }

        private async Task<RepositoryBranch> GetRepositoryBranch(string repo, string branch)
        {
            RepositoryBranch repoBranch = await Context.RepositoryBranches.FindAsync(repo, branch);
            if (repoBranch == null)
            {
                Context.RepositoryBranches.Add(
                    repoBranch = new RepositoryBranch
                    {
                        RepositoryName = repo,
                        BranchName = branch
                    });
            }
            else
            {
                Context.RepositoryBranches.Update(repoBranch);
            }

            return repoBranch;
        }

        [DataContract]
        public class UpdateAssetsParameters
        {
            [DataMember]
            public Guid SubscriptionId { get; set; }

            [DataMember]
            public int BuildId { get; set; }

            [DataMember]
            public string SourceSha { get; set; }

            [DataMember]
            public string SourceRepo { get; set; }

            [DataMember]
            public List<Asset> Assets { get; set; }

            /// <summary>
            ///     If true, this is a coherency update and not driven by specific
            ///     subscription ids (e.g. could be multiple if driven by a batched subscription)
            /// </summary>
            [DataMember]
            public bool IsCoherencyUpdate { get; set; }
        }
    }

    /// <summary>
    ///     A <see cref="PullRequestActorImplementation" /> that reads its Merge Policies and Target information from a
    ///     non-batched subscription object
    /// </summary>
    public class NonBatchedPullRequestActorImplementation : PullRequestActorImplementation
    {
        private readonly Lazy<Task<Subscription>> _lazySubscription;
        private readonly IPullRequestPolicyFailureNotifier _pullRequestPolicyFailureNotifier;

        public NonBatchedPullRequestActorImplementation(
            ActorId id,
            IReminderManager reminders,
            IActorStateManager stateManager,
            IMergePolicyEvaluator mergePolicyEvaluator,
            BuildAssetRegistryContext context,
            IRemoteFactory darcFactory,
            ILoggerFactory loggerFactory,
            IActionRunner actionRunner,
            IActorProxyFactory<ISubscriptionActor> subscriptionActorFactory,
            IPullRequestPolicyFailureNotifier pullRequestPolicyFailureNotifier) : base(
            id,
            reminders,
            stateManager,
            mergePolicyEvaluator,
            context,
            darcFactory,
            loggerFactory,
            actionRunner,
            subscriptionActorFactory)
        {
            _lazySubscription = new Lazy<Task<Subscription>>(RetrieveSubscription);
            _pullRequestPolicyFailureNotifier = pullRequestPolicyFailureNotifier;
        }

        public Guid SubscriptionId => Id.GetGuidId();

        private async Task<Subscription> RetrieveSubscription()
        {
            Subscription subscription = await Context.Subscriptions.FindAsync(SubscriptionId);
            if (subscription == null)
            {
                await Reminders.TryUnregisterReminderAsync(PullRequestCheck);
                await Reminders.TryUnregisterReminderAsync(PullRequestUpdate);
                await StateManager.TryRemoveStateAsync(PullRequest);

                throw new SubscriptionException($"Subscription '{SubscriptionId}' was not found...");
            }

            return subscription;
        }

        private Task<Subscription> GetSubscription()
        {
            return _lazySubscription.Value;
        }
        protected override async Task TagSourceRepositoryGitHubContactsIfPossibleAsync(InProgressPullRequest pr)
        {
            await _pullRequestPolicyFailureNotifier.TagSourceRepositoryGitHubContactsAsync(pr);
        }

        protected override async Task<(string repository, string branch)> GetTargetAsync()
        {
            Subscription subscription = await GetSubscription();
            return (subscription.TargetRepository, subscription.TargetBranch);
        }

        protected override async Task<IReadOnlyList<MergePolicyDefinition>> GetMergePolicyDefinitions()
        {
            Subscription subscription = await GetSubscription();
            return (IReadOnlyList<MergePolicyDefinition>) subscription.PolicyObject.MergePolicies ??
                   Array.Empty<MergePolicyDefinition>();
        }

        public override async Task<(InProgressPullRequest pr, bool canUpdate)> SynchronizeInProgressPullRequestAsync()
        {
            Subscription subscription = await GetSubscription();
            if (subscription == null)
            {
                return (null, false);
            }

            return await base.SynchronizeInProgressPullRequestAsync();
        }
    }

    /// <summary>
    ///     A <see cref="PullRequestActorImplementation" /> for batched subscriptions that reads its Target and Merge Policies
    ///     from the configuration for a repository
    /// </summary>
    public class BatchedPullRequestActorImplementation : PullRequestActorImplementation
    {
        public BatchedPullRequestActorImplementation(
            ActorId id,
            IReminderManager reminders,
            IActorStateManager stateManager,
            IMergePolicyEvaluator mergePolicyEvaluator,
            BuildAssetRegistryContext context,
            IRemoteFactory darcFactory,
            ILoggerFactory loggerFactory,
            IActionRunner actionRunner,
            IActorProxyFactory<ISubscriptionActor> subscriptionActorFactory)
            :
            base(
            id,
            reminders,
            stateManager,
            mergePolicyEvaluator,
            context,
            darcFactory,
            loggerFactory,
            actionRunner,
            subscriptionActorFactory)
        {
        }

        private (string repository, string branch) Target => PullRequestActorId.Parse(Id);

        protected override Task<(string repository, string branch)> GetTargetAsync()
        {
            return Task.FromResult((Target.repository, Target.branch));
        }

        protected override async Task<IReadOnlyList<MergePolicyDefinition>> GetMergePolicyDefinitions()
        {
            RepositoryBranch repositoryBranch =
                await Context.RepositoryBranches.FindAsync(Target.repository, Target.branch);
            return (IReadOnlyList<MergePolicyDefinition>) repositoryBranch?.PolicyObject?.MergePolicies ??
                   Array.Empty<MergePolicyDefinition>();
        }
    }
}
