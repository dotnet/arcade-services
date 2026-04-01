// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Common.Telemetry;
using Maestro.DataProviders;
using Maestro.MergePolicies;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging;
using ProductConstructionService.DependencyFlow.Model;
using ProductConstructionService.DependencyFlow.WorkItems;

using BuildDTO = Microsoft.DotNet.ProductConstructionService.Client.Models.Build;
using SubscriptionDTO = Microsoft.DotNet.ProductConstructionService.Client.Models.Subscription;

namespace ProductConstructionService.DependencyFlow;

internal class CodeFlowPullRequestUpdater : PullRequestUpdater
{
    private readonly IVmrInfo _vmrInfo;
    private readonly IPcsVmrForwardFlower _vmrForwardFlower;
    private readonly IPcsVmrBackFlower _vmrBackFlower;
    private readonly ILocalLibGit2Client _gitClient;
    private readonly IPullRequestBuilder _pullRequestBuilder;
    private readonly IRemoteFactory _remoteFactory;
    private readonly ISqlBarClient _sqlClient;
    private readonly ITelemetryRecorder _telemetryRecorder;
    private readonly ICommentCollector _commentCollector;
    private readonly IPullRequestStateManager _stateManager;
    private readonly ISubscriptionEventRecorder _subscriptionEventRecorder;
    private readonly IPullRequestTarget _target;
    private readonly ILogger<CodeFlowPullRequestUpdater> _logger;

    public CodeFlowPullRequestUpdater(
        IPullRequestTarget target,
        IPullRequestChecker pullRequestChecker,
        IRemoteFactory remoteFactory,
        IPullRequestBuilder pullRequestBuilder,
        ISqlBarClient sqlClient,
        ILocalLibGit2Client gitClient,
        IVmrInfo vmrInfo,
        IPcsVmrForwardFlower vmrForwardFlower,
        IPcsVmrBackFlower vmrBackFlower,
        ITelemetryRecorder telemetryRecorder,
        ICommentCollector commentCollector,
        IPullRequestCommenter pullRequestCommenter,
        IPullRequestStateManager stateManager,
        ISubscriptionEventRecorder subscriptionEventRecorder,
        ILogger<CodeFlowPullRequestUpdater> logger)
        : base(target, pullRequestChecker, sqlClient, pullRequestCommenter, stateManager, logger)
    {
        _vmrInfo = vmrInfo;
        _vmrForwardFlower = vmrForwardFlower;
        _vmrBackFlower = vmrBackFlower;
        _gitClient = gitClient;
        _pullRequestBuilder = pullRequestBuilder;
        _remoteFactory = remoteFactory;
        _sqlClient = sqlClient;
        _telemetryRecorder = telemetryRecorder;
        _commentCollector = commentCollector;
        _logger = logger;
        _stateManager = stateManager;
        _subscriptionEventRecorder = subscriptionEventRecorder;
        _target = target;
    }

    protected async override Task ProcessSubscriptionUpdateAsync(
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

            await _stateManager.SetCheckReminderAsync(pr, prInfo!, isCodeFlow: true);
            await _stateManager.UnsetUpdateReminderAsync(isCodeFlow: true);
            return;
        }

        if (pr?.BlockedFromFutureUpdates == true && !forceUpdate)
        {
            _logger.LogInformation("Failed to update pr {url} for {subscription} because it is blocked from future updates",
                pr.Url,
                update.SubscriptionId);
            await _stateManager.SetCheckReminderAsync(pr, prInfo!, isCodeFlow: true);
            await _stateManager.UnsetUpdateReminderAsync(isCodeFlow: true);
            return;
        }

        var subscription = await _sqlClient.GetSubscriptionAsync(update.SubscriptionId);
        if (subscription == null)
        {
            _logger.LogWarning("Subscription {subscriptionId} was not found. Stopping updates", update.SubscriptionId);
            await _stateManager.ClearAllStateAsync(isCodeFlow: true, clearPendingUpdates: true);
            return;
        }

        var isForwardFlow = subscription.IsForwardFlow();
        string prHeadBranch = pr?.HeadBranch ?? GetNewBranchName(subscription.TargetBranch);

        IRemote remote = await _remoteFactory.CreateRemoteAsync(subscription.TargetRepository);

        IReadOnlyCollection<UpstreamRepoDiff> upstreamRepoDiffs;
        string? previousSourceSha; // is null in some edge cases like onboarding a new repository

        var codeFlowRes = await ExecuteCodeFlowAsync(
            pr,
            prInfo,
            update,
            subscription,
            build,
            prHeadBranch,
            forceUpdate);

        if (codeFlowRes == null)
        {
            return;
        }

        if (!codeFlowRes.HadUpdates)
        {
            return;
        }

        if (isForwardFlow)
        {
            SourceManifest? sourceManifest = await remote.GetSourceManifestAsync(
                subscription.TargetRepository,
                subscription.TargetBranch);

            previousSourceSha = sourceManifest?
                .GetRepoVersion(subscription.TargetDirectory)?.CommitSha;

            upstreamRepoDiffs = [];
        }
        else
        {
            SourceDependency? sourceDependency = await remote.GetSourceDependencyAsync(
                subscription.TargetRepository,
                subscription.TargetBranch);

            previousSourceSha = sourceDependency?.Sha;

            upstreamRepoDiffs = await ComputeRepoUpdatesAsync(previousSourceSha, build.Commit);
        }

        // Conflicts + rebase means we have to block the PR until a human resolves the conflicts manually
        if (codeFlowRes.HadConflicts)
        {
            await RequestManualConflictResolutionAsync(
                update,
                pr,
                previousSourceSha,
                subscription,
                prHeadBranch,
                codeFlowRes,
                upstreamRepoDiffs);

            return;
        }

        if (pr == null)
        {
            await CreateCodeFlowPullRequestAsync(
                update,
                previousSourceSha,
                subscription,
                prHeadBranch,
                codeFlowRes.DependencyUpdates,
                upstreamRepoDiffs);
        }
        else if (pr != null && prInfo != null)
        {
            await UpdateCodeFlowPullRequestAsync(
                update,
                pr,
                prInfo,
                previousSourceSha,
                subscription,
                codeFlowRes.DependencyUpdates,
                upstreamRepoDiffs);
        }
    }

    private async Task<CodeFlowResult?> ExecuteCodeFlowAsync(
        InProgressPullRequest? pr,
        PullRequest? prInfo,
        SubscriptionUpdateWorkItem update,
        SubscriptionDTO subscription,
        BuildDTO build,
        string prHeadBranch,
        bool forceUpdate)
    {
        _logger.LogInformation(
            "{direction}-flowing build {buildId} of {sourceRepo} for subscription {subscriptionId} targeting {targetRepo} / {targetBranch} to new branch {newBranch}",
            subscription.IsForwardFlow() ? "Forward" : "Back",
            build.Id,
            subscription.SourceRepository,
            subscription.Id,
            subscription.TargetRepository,
            subscription.TargetBranch,
            prHeadBranch);

        CodeFlowResult codeFlowRes;
        try
        {
            codeFlowRes = subscription.IsForwardFlow()
                ? await _vmrForwardFlower.FlowForwardAsync(
                    subscription,
                    build,
                    prHeadBranch,
                    forceUpdate,
                    cancellationToken: default)
                : await _vmrBackFlower.FlowBackAsync(
                    subscription,
                    build,
                    prHeadBranch,
                    forceUpdate,
                    cancellationToken: default);
        }
        catch (BlockingCodeflowException) when (pr != null)
        {
            await HandleBlockingCodeflowException(pr);
            return null;
        }
        catch (TargetBranchNotFoundException)
        {
            _logger.LogWarning("Target branch {targetBranch} not found for subscription {subscriptionId}.",
                subscription.TargetBranch,
                subscription.Id);
            return null;
        }
        catch (Exception)
        {
            _logger.LogError("Failed to flow source changes for build {buildId} in subscription {subscriptionId}",
                build.Id,
                subscription.Id);
            throw;
        }

        if (codeFlowRes.HadConflicts)
        {
            _logger.LogInformation("Detected conflicts while rebasing new changes");
            return codeFlowRes;
        }

        if (!codeFlowRes.HadUpdates)
        {
            _logger.LogInformation("There were no code-flow updates for subscription {subscriptionId}", subscription.Id);
            return codeFlowRes;
        }

        _logger.LogInformation("Code changes for {subscriptionId} ready in local branch {branch}",
            subscription.Id,
            prHeadBranch);

        using (var scope = _telemetryRecorder.RecordGitOperation(TrackedGitOperation.Push, subscription.TargetRepository))
        {
            var localTargetRepoPath = subscription.IsForwardFlow() ? _vmrInfo.VmrPath : codeFlowRes.RepoPath;
            await _gitClient.Push(localTargetRepoPath, prHeadBranch, subscription.TargetRepository);
            scope.SetSuccess();
        }

        // We store it the new head branch SHA in Redis (without having to have to query the remote repo)
        prInfo?.HeadBranchSha = await _gitClient.GetShaForRefAsync(subscription.IsForwardFlow() ? _vmrInfo.VmrPath : codeFlowRes.RepoPath, prHeadBranch);

        await _subscriptionEventRecorder.RegisterSubscriptionUpdateAction(SubscriptionUpdateAction.ApplyingUpdates, update.SubscriptionId);

        return codeFlowRes;
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

    private async Task RequestManualConflictResolutionAsync(
        SubscriptionUpdateWorkItem update,
        InProgressPullRequest? pr,
        string? previousSourceSha,
        SubscriptionDTO subscription,
        string prHeadBranch,
        CodeFlowResult codeFlowResult,
        IReadOnlyCollection<UpstreamRepoDiff> upstreamRepoDiffs)
    {
        PullRequest prInfo;
        IRemote remote = await _remoteFactory.CreateRemoteAsync(subscription.TargetRepository);
        NativePath localRepo = subscription.IsForwardFlow() ? _vmrInfo.VmrPath : codeFlowResult.RepoPath;
        bool prIsEmpty;
        string initialCommitMessage = $"Initial commit for subscription {subscription.Id}";

        if (pr == null)
        {
            prIsEmpty = true;
            _logger.LogInformation("Creating PR that requires manual conflict resolution for build {buildId}...", update.BuildId);
            await CreateEmptyPrBranch(subscription, localRepo, prHeadBranch, subscription.TargetBranch, initialCommitMessage);

            (pr, prInfo) = await CreateCodeFlowPullRequestAsync(
                update,
                previousSourceSha,
                subscription,
                prHeadBranch,
                codeFlowResult.DependencyUpdates,
                upstreamRepoDiffs);
        }
        else
        {
            // We get the latest SHAs of the PR branch and the target branch
            var remoteName = (await _gitClient.GetRemotesAsync(localRepo))
                .First(r => r.Uri.Equals(subscription.TargetRepository))
                .Name;
            await _gitClient.UpdateRemoteAsync(localRepo, remoteName);
            var latestPrCommit = await _gitClient.GetShaForRefAsync(localRepo, $"{remoteName}/{prHeadBranch}");
            var latestTargetBranchCommit = await _gitClient.GetShaForRefAsync(localRepo, $"{remoteName}/{subscription.TargetBranch}");

            // We check if the PR branch still only contains the empty commit only
            var latestCommitMessage = await _gitClient.RunGitCommandAsync(localRepo, [$"log", "-1", "--pretty=%B", latestPrCommit]);
            prIsEmpty = latestCommitMessage.StandardOutput.Trim().StartsWith(initialCommitMessage);

            // When the PR is empty but a new build has flown in, we should rebase the PR branch onto the target branch and force-push
            if (prIsEmpty && !await _gitClient.IsAncestorCommit(localRepo, latestTargetBranchCommit, latestPrCommit))
            {
                _logger.LogInformation("Rebasing empty PR branch {headBranch} onto {targetBranch}", prHeadBranch, subscription.TargetBranch);
                await CreateEmptyPrBranch(subscription, localRepo, prHeadBranch, latestTargetBranchCommit, initialCommitMessage);
            }

            prInfo = await remote.GetPullRequestAsync(pr.Url)
                ?? throw new DarcException($"Failed to retrieve PR info for existing PR {pr.Url} while requesting manual conflict resolution");

            _logger.LogInformation("Notifying PR that it requires manual conflict resolution for build {buildId}...", update.BuildId);

            await UpdateCodeFlowPullRequestAsync(
                update,
                pr,
                prInfo,
                previousSourceSha,
                subscription,
                codeFlowResult.DependencyUpdates,
                upstreamRepoDiffs);

            // Since we changed the PR state in cache but no commit was pushed,
            // we need to delete non-transient check results so that they can be re-evaluated
            await _stateManager.ClearMergePolicyEvaluationStateAsync();
        }

        _commentCollector.AddComment(
            PullRequestCommentBuilder.BuildNotificationAboutManualConflictResolutionComment(
                update,
                subscription,
                codeFlowResult.ConflictedFiles,
                prHeadBranch,
                prIsEmpty),
            CommentType.Caution);

        // We know for sure that we will fail the codeflow checks (codeflow metadata will be expected to match the new build)
        // So we trigger the evaluation right away
        await RunMergePolicyEvaluation(pr, prInfo, remote);
    }

    /// <summary>
    /// Creates and pushes a new branch.
    /// It must have an empty commit to differentiate it from the target branch so that an empty PR can be created.
    /// </summary>
    private async Task CreateEmptyPrBranch(
        SubscriptionDTO subscription,
        NativePath localRepo,
        string prBranchName,
        string baseCommit,
        string initialCommitMessage)
    {
        await _gitClient.ForceCheckoutAsync(localRepo, baseCommit);
        await _gitClient.CreateBranchAsync(localRepo, prBranchName, overwriteExistingBranch: true);
        await _gitClient.CommitAsync(localRepo, initialCommitMessage, allowEmpty: true);
        await _gitClient.Push(localRepo, prBranchName, subscription.TargetRepository, force: true);
    }

    private async Task<(InProgressPullRequest, PullRequest)> CreateCodeFlowPullRequestAsync(
        SubscriptionUpdateWorkItem update,
        string? previousSourceSha,
        SubscriptionDTO subscription,
        string prBranch,
        List<DependencyUpdate> dependencyUpdates,
        IReadOnlyCollection<UpstreamRepoDiff>? upstreamRepoDiffs)
    {
        IRemote darcRemote = await _remoteFactory.CreateRemoteAsync(subscription.TargetRepository);
        var build = await _sqlClient.GetBuildAsync(update.BuildId);
        List<DependencyUpdateSummary> requiredUpdates = [.. dependencyUpdates.Select(du => new DependencyUpdateSummary(du))];
        try
        {
            var title = _pullRequestBuilder.GenerateCodeFlowPRTitle(subscription.TargetBranch, [update.SourceRepo]);
            var description = await _pullRequestBuilder.GenerateCodeFlowPRDescription(
                build,
                subscription,
                prBranch,
                previousSourceSha,
                requiredUpdates,
                upstreamRepoDiffs,
                currentDescription: null);

            PullRequest pr = await darcRemote.CreatePullRequestAsync(
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
                UpdaterId = _target.UpdaterId,
                Url = pr.Url,
                HeadBranch = prBranch,
                HeadBranchSha = pr.HeadBranchSha,
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
                CodeFlowDirection = subscription.IsForwardFlow()
                    ? CodeFlowDirection.ForwardFlow
                    : CodeFlowDirection.BackFlow,
                CreationDate = DateTime.UtcNow,
            };

            await _subscriptionEventRecorder.AddDependencyFlowEventsAsync(
                inProgressPr.ContainedSubscriptions,
                DependencyFlowEventType.Created,
                DependencyFlowEventReason.New,
                MergePolicyCheckResult.PendingPolicies,
                pr.Url);

            inProgressPr.LastUpdate = DateTime.UtcNow;
            await _stateManager.SetCheckReminderAsync(inProgressPr, pr, isCodeFlow: true);
            await _stateManager.UnsetUpdateReminderAsync(isCodeFlow: true);

            _logger.LogInformation("Code flow pull request created: {prUrl}", pr.Url);

            return (inProgressPr, pr);
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
    /// Updates the PR's title and description
    /// </summary>
    private async Task UpdateCodeFlowPullRequestAsync(
        SubscriptionUpdateWorkItem update,
        InProgressPullRequest pullRequest,
        PullRequest prInfo,
        string? previousSourceSha,
        SubscriptionDTO subscription,
        List<DependencyUpdate> newDependencyUpdates,
        IReadOnlyCollection<UpstreamRepoDiff>? upstreamRepoDiffs)
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

        pullRequest.RequiredUpdates = MergeExistingWithIncomingUpdates(
            pullRequest.RequiredUpdates,
            [.. newDependencyUpdates.Select(u => new DependencyUpdateSummary(u))]);

        var title = _pullRequestBuilder.GenerateCodeFlowPRTitle(
            subscription.TargetBranch,
            [.. pullRequest.ContainedSubscriptions.Select(s => s.SourceRepo)]);

        var description = await _pullRequestBuilder.GenerateCodeFlowPRDescription(
            build,
            subscription,
            prInfo.HeadBranch,
            previousSourceSha,
            pullRequest.RequiredUpdates,
            upstreamRepoDiffs,
            prInfo?.Description);

        try
        {
            await remote.UpdatePullRequestAsync(pullRequest.Url, new PullRequest
            {
                Title = title,
                Description = description
            });

            _logger.LogInformation("Code flow pull request updated: {prUrl}", pullRequest.Url);
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
            // Even if we fail to update the PR title and description, the changes already got pushed, so we want to enqueue a PullRequestCheck
            pullRequest.SourceSha = update.SourceSha;
            pullRequest.LastUpdate = DateTime.UtcNow;
            pullRequest.NextBuildsToProcess.Remove(update.SubscriptionId);
            pullRequest.BlockedFromFutureUpdates = false; // if a sub is blocked, and someone force triggers it, we can continue flowing afterwards
            await _stateManager.SetCheckReminderAsync(pullRequest, prInfo!, isCodeFlow: true);
            await _stateManager.UnsetUpdateReminderAsync(isCodeFlow: true);
        }
    }

    private async Task HandleBlockingCodeflowException(InProgressPullRequest pr)
    {
        _logger.LogInformation("PR with url {prUrl} is blocked from receiving future codeflows.", pr.Url);
        _commentCollector.AddComment(PullRequestCommentBuilder.BuildOppositeCodeflowMergedNotification(), CommentType.Warning);
        pr.BlockedFromFutureUpdates = true;
        await _stateManager.SetInProgressPullRequestAsync(pr);
    }
}

// <summary>
// Contains the old and new SHAs of an upstream repo (repo that the product repo depends on)
// </summary>
public record UpstreamRepoDiff(
    string RepoUri,
    string? OldCommitSha,
    string? NewCommitSha);
