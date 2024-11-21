// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Policy;
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
using Microsoft.VisualStudio.Services.Common;
using ProductConstructionService.Client;
using ProductConstructionService.Client.Models;
using SubscriptionActorService.StateModel;

using Asset = Maestro.Contracts.Asset;
using AssetData = Microsoft.DotNet.Maestro.Client.Models.AssetData;

#nullable enable
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

            public Task UpdateAssetsAsync(Guid subscriptionId, SubscriptionType type, int buildId, string sourceRepo, string sourceSha, List<Asset> assets)
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
    internal class PullRequestActor : IPullRequestActor, IRemindable, IActionTracker, IActorImplementation
    {
        private readonly IMergePolicyEvaluator _mergePolicyEvaluator;
        private readonly BuildAssetRegistryContext _context;
        private readonly IRemoteFactory _darcFactory;
        private readonly IProductConstructionServiceApi _pcsClient;
        private readonly ICoherencyUpdateResolver _coherencyUpdateResolver;
        private readonly IPullRequestBuilder _pullRequestBuilder;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IActionRunner _actionRunner;
        private readonly IActorProxyFactory<ISubscriptionActor> _subscriptionActorFactory;
        private readonly IPullRequestPolicyFailureNotifier _pullRequestPolicyFailureNotifier;

        /// <summary>
        ///     Creates a new PullRequestActor
        /// </summary>
        public PullRequestActor(
            IMergePolicyEvaluator mergePolicyEvaluator,
            BuildAssetRegistryContext context,
            IRemoteFactory darcFactory,
            IProductConstructionServiceApi pcsClient,
            ICoherencyUpdateResolver coherencyUpdateResolver,
            IPullRequestBuilder pullRequestBuilder,
            ILoggerFactory loggerFactory,
            IActionRunner actionRunner,
            IActorProxyFactory<ISubscriptionActor> subscriptionActorFactory,
            IPullRequestPolicyFailureNotifier pullRequestPolicyFailureNotifier)
        {
            _mergePolicyEvaluator = mergePolicyEvaluator;
            _context = context;
            _darcFactory = darcFactory;
            _pcsClient = pcsClient;
            _coherencyUpdateResolver = coherencyUpdateResolver;
            _pullRequestBuilder = pullRequestBuilder;
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
            => actorId.Kind switch
            {
                ActorIdKind.Guid => new NonBatchedPullRequestActorImplementation(
                    actorId,
                    reminderManager,
                    stateManager,
                    _mergePolicyEvaluator,
                    _coherencyUpdateResolver,
                    _context,
                    _darcFactory,
                    _pcsClient,
                    _pullRequestBuilder,
                    _loggerFactory,
                    _actionRunner,
                    _subscriptionActorFactory,
                    _pullRequestPolicyFailureNotifier),

                ActorIdKind.String => new BatchedPullRequestActorImplementation(
                    actorId,
                    reminderManager,
                    stateManager,
                    _mergePolicyEvaluator,
                    _coherencyUpdateResolver,
                    _context,
                    _darcFactory,
                    _pcsClient,
                    _pullRequestBuilder,
                    _loggerFactory,
                    _actionRunner,
                    _subscriptionActorFactory),

                _ => throw new NotSupportedException($"Only actorIds of type {nameof(ActorIdKind.Guid)} and {nameof(ActorIdKind.String)} are supported"),
            };

        public PullRequestActorImplementation? Implementation { get; private set; }

        public Task TrackSuccessfulAction(string action, string result)
        {
            return Implementation!.TrackSuccessfulAction(action, result);
        }

        public Task TrackFailedAction(string action, string result, string method, string arguments)
        {
            return Implementation!.TrackFailedAction(action, result, method, arguments);
        }

        public Task<string> RunActionAsync(string method, string arguments)
        {
            return Implementation!.RunActionAsync(method, arguments);
        }

        public Task UpdateAssetsAsync(Guid subscriptionId, SubscriptionType type, int buildId, string sourceRepo, string sourceSha, List<Asset> assets)
        {
            return Implementation!.UpdateAssetsAsync(subscriptionId, type, buildId, sourceRepo, sourceSha, assets);
        }

        public async Task ReceiveReminderAsync(string reminderName, byte[] state, TimeSpan dueTime, TimeSpan period)
        {
            switch (reminderName)
            {
                case PullRequestActorImplementation.PullRequestCheckKey:
                    await Implementation!.SynchronizeInProgressPullRequestAsync();
                    break;
                case PullRequestActorImplementation.PullRequestUpdateKey:
                    await Implementation!.RunProcessPendingUpdatesAsync();
                    break;
                default:
                    throw new ReminderNotFoundException(reminderName);
            }
        }
    }

    internal abstract class PullRequestActorImplementation : IPullRequestActor, IActionTracker
    {
        // Actor state keys
        public const string PullRequestCheckKey = "pullRequestCheck";
        public const string PullRequestUpdateKey = "pullRequestUpdate";
        public const string PullRequestKey = "pullRequest";
        public const string CodeFlowKey = "codeFlow";

        private readonly ILogger _logger;
        private readonly IMergePolicyEvaluator _mergePolicyEvaluator;
        private readonly BuildAssetRegistryContext _context;
        private readonly IRemoteFactory _remoteFactory;
        private readonly IProductConstructionServiceApi _pcsClient;
        private readonly IPullRequestBuilder _pullRequestBuilder;
        private readonly IActionRunner _actionRunner;
        private readonly IActorProxyFactory<ISubscriptionActor> _subscriptionActorFactory;
        private readonly ICoherencyUpdateResolver _coherencyUpdateResolver;

        protected readonly ActorCollectionStateManager<UpdateAssetsParameters> _pullRequestUpdateState;
        protected readonly ActorReminderManager<UpdateAssetsParameters> _pullRequestCheckState;
        protected readonly ActorStateManager<InProgressPullRequest> _pullRequestState;
        protected readonly ActorStateManager<CodeFlowStatus> _codeFlowState;

        protected PullRequestActorImplementation(
            IReminderManager reminders,
            IActorStateManager stateManager,
            IMergePolicyEvaluator mergePolicyEvaluator,
            ICoherencyUpdateResolver coherencyUpdateResolver,
            BuildAssetRegistryContext context,
            IRemoteFactory darcFactory,
            IProductConstructionServiceApi pcsClient,
            IPullRequestBuilder pullRequestBuilder,
            ILoggerFactory loggerFactory,
            IActionRunner actionRunner,
            IActorProxyFactory<ISubscriptionActor> subscriptionActorFactory)
        {
            _mergePolicyEvaluator = mergePolicyEvaluator;
            _coherencyUpdateResolver = coherencyUpdateResolver;
            _context = context;
            _remoteFactory = darcFactory;
            _pcsClient = pcsClient;
            _pullRequestBuilder = pullRequestBuilder;
            _actionRunner = actionRunner;
            _subscriptionActorFactory = subscriptionActorFactory;
            _logger = loggerFactory.CreateLogger(GetType());

            _pullRequestUpdateState = new(stateManager, reminders, _logger, PullRequestUpdateKey);
            _pullRequestCheckState = new(reminders, PullRequestCheckKey);
            _pullRequestState = new(stateManager, _logger, PullRequestKey);
            _codeFlowState = new(stateManager, _logger, CodeFlowKey);
        }

        public async Task TrackSuccessfulAction(string action, string result)
        {
            RepositoryBranchUpdate update = await GetRepositoryBranchUpdate();

            update.Action = action;
            update.ErrorMessage = result;
            update.Method = null;
            update.Arguments = null;
            update.Success = true;
            await _context.SaveChangesAsync();
        }

        public async Task TrackFailedAction(string action, string result, string method, string arguments)
        {
            RepositoryBranchUpdate update = await GetRepositoryBranchUpdate();

            update.Action = action;
            update.ErrorMessage = result;
            update.Method = method;
            update.Arguments = arguments;
            update.Success = false;
            await _context.SaveChangesAsync();
        }

        public Task<string> RunActionAsync(string method, string arguments)
        {
            return _actionRunner.RunAction(this, method, arguments);
        }

        Task IPullRequestActor.UpdateAssetsAsync(Guid subscriptionId, SubscriptionType type, int buildId, string sourceRepo, string sourceSha, List<Asset> assets)
        {
            return _actionRunner.ExecuteAction(() => UpdateAssetsAsync(subscriptionId, type, buildId, sourceRepo, sourceSha, assets));
        }

        protected abstract Task<(string repository, string branch)> GetTargetAsync();

        protected abstract Task<IReadOnlyList<MergePolicyDefinition>> GetMergePolicyDefinitions();

        public Task RunProcessPendingUpdatesAsync()
        {
            return _actionRunner.ExecuteAction(() => ProcessPendingUpdatesAsync());
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
            _logger.LogInformation("Processing pending updates");
            List<UpdateAssetsParameters>? updates = await _pullRequestUpdateState.TryGetStateAsync();
            if (updates == null || updates.Count < 1)
            {
                _logger.LogInformation("No Pending Updates");
                await _pullRequestUpdateState.UnsetReminderAsync();
                return ActionResult.Create(false, "No Pending Updates");
            }

            (InProgressPullRequest? pr, bool canUpdate) = await SynchronizeInProgressPullRequestAsync();

            // Code flow updates are handled separetely
            if (updates.Any(u => u.Type == SubscriptionType.DependenciesAndSources))
            {
                return await ProcessCodeFlowUpdatesAsync(updates, pr);
            }

            var subscriptionIds = updates.Count > 1
                ? "subscriptions " + string.Join(", ", updates.Select(u => u.SubscriptionId).Distinct())
                : "subscription " + updates[0].SubscriptionId;

            string result;
            if (pr == null)
            {
                // Create regular dependency update PR
                string? prUrl = await CreatePullRequestAsync(updates);
                result = prUrl == null
                    ? $"No changes required for {subscriptionIds}, no pull request created"
                    : $"Pull Request '{prUrl}' for {subscriptionIds} created";

                _logger.LogInformation(result);

                await _pullRequestUpdateState.RemoveStateAsync();
                await _pullRequestUpdateState.UnsetReminderAsync();

                return ActionResult.Create(true, "Pending updates applied. " + result);
            }

            if (!canUpdate)
            {
                _logger.LogInformation("PR {url} for {subscriptions} cannot be updated", pr.Url, subscriptionIds);
                return ActionResult.Create(false, "PR cannot be updated.");
            }

            await UpdatePullRequestAsync(pr, updates);
            result = $"Pull Request '{pr.Url}' updated.";
            _logger.LogInformation("Pull Request {url} for {subscriptions} was updated", pr.Url, subscriptionIds);

            await _pullRequestUpdateState.RemoveStateAsync();
            await _pullRequestUpdateState.UnsetReminderAsync();

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

            SynchronizePullRequestResult result = await _actionRunner.ExecuteAction(() => SynchronizePullRequestAsync(pr.Url));

            _logger.LogInformation("Pull Request {url} is {result}", pr.Url, result);

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
        [ActionMethod("Synchronizing Pull Request: '{url}'")]
        private async Task<ActionResult<SynchronizePullRequestResult>> SynchronizePullRequestAsync(string prUrl)
        {
            _logger.LogInformation("Synchronizing Pull Request {url}", prUrl);

            InProgressPullRequest? pr = await _pullRequestState.TryGetStateAsync();

            if (pr == null)
            {
                await _pullRequestCheckState.UnsetReminderAsync();
                return ActionResult.Create(
                    SynchronizePullRequestResult.Invalid,
                    $"Invalid state detected for Pull Request '{prUrl}'");
            }

            if (pr?.Url != prUrl)
            {
                _logger.LogInformation("Not Applicable: Pull Request {url} is not tracked by maestro anymore.", prUrl);
                return ActionResult.Create(
                    SynchronizePullRequestResult.UnknownPR,
                    $"Not Applicable: Pull Request '{prUrl}' is not tracked by maestro anymore.");
            }

            (string targetRepository, _) = await GetTargetAsync();
            IRemote remote = await _remoteFactory.GetRemoteAsync(targetRepository, _logger);

            _logger.LogInformation("Getting status for Pull Request: {url}", prUrl);
            PrStatus status = await remote.GetPullRequestStatusAsync(prUrl);
            _logger.LogInformation("Pull Request {url} is {status}", prUrl, status);
            switch (status)
            {
                // If the PR is currently open, then evaluate the merge policies, which will potentially
                // merge the PR if they are successful.
                case PrStatus.Open:
                    ActionResult<MergePolicyCheckResult> checkPolicyResult = await CheckMergePolicyAsync(pr, remote);
                    pr.MergePolicyResult = checkPolicyResult.Result;

                    _logger.LogInformation("Policy check status for Pull Request {url} is {result}", prUrl, checkPolicyResult.Result);

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

                            await _pullRequestState.RemoveStateAsync();
                            await _codeFlowState.RemoveStateAsync();

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

                    await _pullRequestState.RemoveStateAsync();
                    await _codeFlowState.RemoveStateAsync();

                    // Also try to clean up the PR branch.
                    try
                    {
                        _logger.LogInformation("Trying to clean up the branch for Pull Request {url}", prUrl);
                        await remote.DeletePullRequestBranchAsync(prUrl);
                    }
                    catch (DarcException)
                    {
                        _logger.LogInformation("Failed to delete branch associated with Pull Request {url}", prUrl);
                    }

                    return ActionResult.Create(SynchronizePullRequestResult.Completed, $"PR has been manually {status}");

                default:
                    throw new NotImplementedException($"Unknown PR status '{status}'");
            }
        }

        /// <summary>
        ///     Check the merge policies for a PR and merge if they have succeeded.
        /// </summary>
        /// <param name="prUrl">Pull request URL</param>
        /// <param name="remote">Darc remote</param>
        /// <returns>Result of the policy check.</returns>
        private async Task<ActionResult<MergePolicyCheckResult>> CheckMergePolicyAsync(IPullRequest pr, IRemote remote)
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

                return ActionResult.Create(
                    MergePolicyCheckResult.FailedPolicies,
                    $"NOT Merged: PR '{pr.Url}' has failed policies {string.Join(", ", result.Results.Where(r => r.Status == MergePolicyEvaluationStatus.Failure).Select(r => r.MergePolicyInfo.Name + r.Title))}");
            }

            if (result.Pending)
            {
                return ActionResult.Create(
                    MergePolicyCheckResult.PendingPolicies,
                    $"NOT Merged: PR '{pr.Url}' has pending policies {string.Join(", ", result.Results.Where(r => r.Status == MergePolicyEvaluationStatus.Pending).Select(r => r.MergePolicyInfo.Name + r.Title))}");
            }

            if (!result.Succeeded)
            {
                _logger.LogInformation("NOT Merged: PR '{url}' There are no merge policies", pr.Url);
                return ActionResult.Create(MergePolicyCheckResult.NoPolicies, "NOT Merged: There are no merge policies");
            }

            try
            {
                await remote.MergeDependencyPullRequestAsync(pr.Url, new MergePullRequestParameters());
            }
            catch (PullRequestNotMergeableException notMergeableException)
            {
                _logger.LogInformation("NOT Merged: PR '{url}' cannot be merged. - {message}", pr.Url, notMergeableException.Message);
                return ActionResult.Create(MergePolicyCheckResult.FailedToMerge, $"NOT Merged: PR '{pr.Url}' cannot be merged.");
            }

            string passedPolicies = string.Join(", ", policyDefinitions.Select(p => p.Name));
            _logger.LogInformation("Merged: PR '{url}' passed policies {passedPolicies}", pr.Url, passedPolicies);
            return ActionResult.Create(
                MergePolicyCheckResult.Merged,
                $"Merged: PR '{pr.Url}' passed policies {passedPolicies}");
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
                ISubscriptionActor actor = _subscriptionActorFactory.Lookup(new ActorId(update.SubscriptionId));
                if (!await actor.UpdateForMergedPullRequestAsync(update.BuildId))
                {
                    _logger.LogInformation("Failed to update subscription {subscriptionId} for merged PR.", update.SubscriptionId);
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
                ISubscriptionActor actor = _subscriptionActorFactory.Lookup(new ActorId(update.SubscriptionId));
                if (!await actor.AddDependencyFlowEventAsync(update.BuildId, flowEvent, reason, policy, "PR", prUrl))
                {
                    _logger.LogInformation($"Failed to add dependency flow event for {update.SubscriptionId}.");
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
        [ActionMethod("Updating assets for subscription: {subscriptionId}, build: {buildId}")]
        public async Task<ActionResult<object>> UpdateAssetsAsync(
            Guid subscriptionId,
            SubscriptionType type,
            int buildId,
            string sourceRepo,
            string sourceSha,
            List<Asset> assets)
        {
            (InProgressPullRequest? pr, bool canUpdate) = await SynchronizeInProgressPullRequestAsync();

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

                return ActionResult.Create($"Current Pull request '{pr.Url}' cannot be updated, update queued.");
            }

            if (type == SubscriptionType.DependenciesAndSources)
            {
                var result = await ProcessCodeFlowUpdatesAsync([updateParameter], pr);
                return ActionResult.Create(result.Message);
            }

            try
            {
                if (pr == null)
                {
                    string? prUrl = await CreatePullRequestAsync([updateParameter]);
                    return prUrl == null
                        ? ActionResult.Create("Updates require no changes, no pull request created.")
                        : ActionResult.Create($"Pull request '{prUrl}' created.");
                }

                await UpdatePullRequestAsync(pr, [updateParameter]);
                return ActionResult.Create($"Pull Request '{pr.Url}' updated.");
            }
            catch (HttpRequestException reqEx) when (reqEx.Message.Contains(((int) HttpStatusCode.Unauthorized).ToString()))
            {
                // We want to preserve the HttpRequestException's information but it's not serializable
                // We'll log the full exception object so it's in Application Insights, and strip any single quotes from the message to ensure 
                // GitHub issues are properly created.
                _logger.LogError(reqEx, "Failure to authenticate to repository");
                throw new DarcAuthenticationFailureException($"Failure to authenticate: {reqEx.Message}");
            }
        }

        /// <summary>
        ///     Creates a pull request from the given updates.
        /// </summary>
        /// <returns>The pull request url when a pr was created; <see langref="null" /> if no PR is necessary</returns>
        private async Task<string?> CreatePullRequestAsync(List<UpdateAssetsParameters> updates)
        {
            (string targetRepository, string targetBranch) = await GetTargetAsync();

            IRemote darcRemote = await _remoteFactory.GetRemoteAsync(targetRepository, _logger);

            TargetRepoDependencyUpdate repoDependencyUpdate =
                await GetRequiredUpdates(updates, _remoteFactory, targetRepository, prBranch: null, targetBranch);

            if (repoDependencyUpdate.CoherencyCheckSuccessful && repoDependencyUpdate.RequiredUpdates.Count < 1)
            {
                return null;
            }

            string newBranchName = GetNewBranchName(targetBranch);
            await darcRemote.CreateNewBranchAsync(targetRepository, targetBranch, newBranchName);

            try
            {
                string description = await _pullRequestBuilder.CalculatePRDescriptionAndCommitUpdatesAsync(
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

                string prUrl = await darcRemote.CreatePullRequestAsync(
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
            (string targetRepository, string targetBranch) = await GetTargetAsync();

            _logger.LogInformation("Updating Pull Request {url} branch {targetBranch} in {targetRepository}", pr.Url, targetBranch, targetRepository);

            IRemote darcRemote = await _remoteFactory.GetRemoteAsync(targetRepository, _logger);
            PullRequest pullRequest = await darcRemote.GetPullRequestAsync(pr.Url);

            TargetRepoDependencyUpdate targetRepositoryUpdates =
                await GetRequiredUpdates(updates, _remoteFactory, targetRepository, pullRequest.HeadBranch, targetBranch);

            if (targetRepositoryUpdates.CoherencyCheckSuccessful && targetRepositoryUpdates.RequiredUpdates.Count < 1)
            {
                _logger.LogInformation("No updates found for Pull Request {url}", pr.Url);
                return;
            }

            _logger.LogInformation("Found {count} required updates for Pull Request {url}", targetRepositoryUpdates.RequiredUpdates.Count, pr.Url);

            pr.RequiredUpdates = MergeExistingWithIncomingUpdates(pr.RequiredUpdates, targetRepositoryUpdates.RequiredUpdates);

            if (pr.RequiredUpdates.Count < 1)
            {
                _logger.LogInformation("No new updates found for Pull Request {url}", pr.Url);
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

            var requiredDescriptionUpdates =
                await CalculateOriginalDependencies(darcRemote, targetRepository, targetBranch, targetRepositoryUpdates);

            pullRequest.Description = await _pullRequestBuilder.CalculatePRDescriptionAndCommitUpdatesAsync(
                requiredDescriptionUpdates,
                pullRequest.Description,
                targetRepository,
                pullRequest.HeadBranch);

            pullRequest.Title = await _pullRequestBuilder.GeneratePRTitleAsync(pr, targetBranch);

            await darcRemote.UpdatePullRequestAsync(pr.Url, pullRequest);
            await _pullRequestState.StoreStateAsync(pr);
            await _pullRequestCheckState.SetReminderAsync();
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
            public List<CoherencyErrorDetails>? CoherencyErrors { get; set; }
            public List<(UpdateAssetsParameters update, List<DependencyUpdate> deps)> RequiredUpdates { get; set; } = [];
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
            string? prBranch,
            string targetBranch)
        {
            _logger.LogInformation("Getting Required Updates for {branch} of {targetRepository}", targetBranch, targetRepository);
            // Get a remote factory for the target repo
            IRemote darc = await remoteFactory.GetRemoteAsync(targetRepository, _logger);

            TargetRepoDependencyUpdate repoDependencyUpdate = new();

            // Existing details 
            List<DependencyDetail> existingDependencies = (await darc.GetDependenciesAsync(targetRepository, prBranch ?? targetBranch)).ToList();

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
                _logger.LogInformation($"Running a coherency check on the existing dependencies for branch {targetBranch} of repo {targetRepository}");
                coherencyUpdates = await _coherencyUpdateResolver.GetRequiredCoherencyUpdatesAsync(existingDependencies, remoteFactory);
            }
            catch (DarcCoherencyException e)
            {
                _logger.LogInformation("Failed attempting strict coherency update on branch '{strictCoherencyFailedBranch}' of repo '{strictCoherencyFailedRepo}'.",
                     targetBranch, targetRepository);
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
            (string repo, string branch) = await GetTargetAsync();
            RepositoryBranchUpdate? update = await _context.RepositoryBranchUpdates.FindAsync(repo, branch);
            if (update == null)
            {
                var repoBranch = await GetRepositoryBranch(repo, branch);
                _context.RepositoryBranchUpdates.Add(
                    update = new RepositoryBranchUpdate { RepositoryBranch = repoBranch });
            }
            else
            {
                _context.RepositoryBranchUpdates.Update(update);
            }

            return update;
        }

        private async Task<Maestro.Data.Models.RepositoryBranch> GetRepositoryBranch(string repo, string branch)
        {
            var repoBranch = await _context.RepositoryBranches.FindAsync(repo, branch);
            if (repoBranch == null)
            {
                _context.RepositoryBranches.Add(
                    repoBranch = new Maestro.Data.Models.RepositoryBranch
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

        /// <summary>
        ///     Given a set of updates, replace the `from` version of every dependency update with the corresponding version
        ///     from the target branch 
        /// </summary>
        /// <param name="darcRemote">Darc client used to fetch target branch dependencies.</param>
        /// <param name="targetRepository">Target repository to fetch the dependencies from.</param>
        /// <param name="targetBranch">Target branch to fetch the dependencies from.</param>
        /// <param name="targetRepositoryUpdates">Incoming updates to the repository</param>
        /// <returns>
        ///     Asset update and the corresponding list of altered dependencies
        /// </returns>
        /// <remarks>
        ///     This method is intended for use in situations where we want to keep the information about the original dependency
        ///     version, such as when updating PR descriptions.
        /// </remarks>
        private static async Task<List<(UpdateAssetsParameters update, List<DependencyUpdate> deps)>> CalculateOriginalDependencies(
            IRemote darcRemote,
            string targetRepository,
            string targetBranch,
            TargetRepoDependencyUpdate targetRepositoryUpdates)
        {
            List<DependencyDetail> targetBranchDeps = [..await darcRemote.GetDependenciesAsync(targetRepository, targetBranch)];

            List<(UpdateAssetsParameters update, List<DependencyUpdate> deps)> alteredUpdates = [];
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
        private Task<ActionResult<bool>> ProcessCodeFlowUpdatesAsync(
            List<UpdateAssetsParameters> updates,
            InProgressPullRequest? pr)
        {
            _logger.LogWarning("Code flow updates cannot be batched with other updates. Will process the last update only.");
            return Task.FromResult(ActionResult.Create(false, $"Code flow subscriptions are not supported in Maestro"));
        }

        #endregion
    }
}
