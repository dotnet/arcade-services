// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Common.Telemetry;
using Maestro.Data.Models;
using Maestro.WorkItems;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging;
using ProductConstructionService.DependencyFlow.Model;
using ProductConstructionService.DependencyFlow.WorkItems;

using BuildDTO = Microsoft.DotNet.ProductConstructionService.Client.Models.Build;
using SubscriptionDTO = Microsoft.DotNet.ProductConstructionService.Client.Models.Subscription;

namespace ProductConstructionService.DependencyFlow;

internal interface ICodeFlowExecutor
{
    Task<CodeFlowExecutionResult> ExecuteCodeFlowAsync(
        InProgressPullRequest? pr,
        PullRequest? prInfo,
        SubscriptionUpdateWorkItem update,
        SubscriptionDTO subscription,
        BuildDTO build,
        bool forceUpdate);
}

internal enum CodeFlowManualInterventionReason
{
    Conflict,
    RecreationFallbackLimitReached,
}

internal record CodeFlowExecutionResult(
    CodeFlowResult? CodeFlowResult,
    bool UnsafeFlown,
    string PullRequestHeadBranch,
    NativePath TargetRepoPath,
    CodeFlowManualInterventionReason? ManualInterventionReason);

internal class CodeFlowExecutor : ICodeFlowExecutor
{
    private static readonly TimeSpan AutoForceCodeFlowAfter = TimeSpan.FromDays(30);

    private readonly IVmrInfo _vmrInfo;
    private readonly IPcsVmrForwardFlower _vmrForwardFlower;
    private readonly IPcsVmrBackFlower _vmrBackFlower;
    private readonly IVmrCloneManager _vmrCloneManager;
    private readonly IRepositoryCloneManager _repositoryCloneManager;
    private readonly ILocalLibGit2Client _gitClient;
    private readonly ITelemetryRecorder _telemetryRecorder;
    private readonly ISubscriptionEventRecorder _subscriptionEventRecorder;
    private readonly ILogger<CodeFlowExecutor> _logger;

    public CodeFlowExecutor(
        IVmrInfo vmrInfo,
        IPcsVmrForwardFlower vmrForwardFlower,
        IPcsVmrBackFlower vmrBackFlower,
        IVmrCloneManager vmrCloneManager,
        IRepositoryCloneManager repositoryCloneManager,
        ILocalLibGit2Client gitClient,
        ITelemetryRecorder telemetryRecorder,
        ISubscriptionEventRecorder subscriptionEventRecorder,
        ILogger<CodeFlowExecutor> logger)
    {
        _vmrInfo = vmrInfo;
        _vmrForwardFlower = vmrForwardFlower;
        _vmrBackFlower = vmrBackFlower;
        _vmrCloneManager = vmrCloneManager;
        _repositoryCloneManager = repositoryCloneManager;
        _gitClient = gitClient;
        _telemetryRecorder = telemetryRecorder;
        _subscriptionEventRecorder = subscriptionEventRecorder;
        _logger = logger;
    }

    public async Task<CodeFlowExecutionResult> ExecuteCodeFlowAsync(
        InProgressPullRequest? pr,
        PullRequest? prInfo,
        SubscriptionUpdateWorkItem update,
        SubscriptionDTO subscription,
        BuildDTO build,
        bool forceUpdate)
    {
        if (pr != null)
        {
            await RebaseEmptyPRsOnTargetBranchAsync(subscription, pr);
        }

        // If a forward flow subscription had only meaningless changes for a month, we force a codeflow so we're not too far behind.
        forceUpdate |= pr == null
            && subscription.LastAppliedBuild != null
            && subscription.IsForwardFlow()
            && DateTimeOffset.UtcNow - subscription.LastAppliedBuild.DateProduced > AutoForceCodeFlowAfter;

        string prHeadBranch = pr?.HeadBranch ?? GetNewBranchName(subscription.TargetBranch);

        _logger.LogInformation(
            "{direction}-flowing build {buildId} of {sourceRepo} for subscription {subscriptionId} targeting {targetRepo} / {targetBranch} to new branch {newBranch}",
            subscription.IsForwardFlow() ? "Forward" : "Back",
            build.Id,
            subscription.SourceRepository,
            subscription.Id,
            subscription.TargetRepository,
            subscription.TargetBranch,
            prHeadBranch);

        CodeFlowExecutionResult executionResult = await ExecuteWithUnsafeFallbackAsync(
            subscription,
            build,
            prHeadBranch,
            forceUpdate);

        if (executionResult.ManualInterventionReason != null)
        {
            return executionResult;
        }

        CodeFlowResult codeFlowRes = executionResult.CodeFlowResult
            ?? throw new InvalidOperationException("Completed codeflow did not return a result");
        bool unsafeFlown = executionResult.UnsafeFlown;
        prHeadBranch = executionResult.PullRequestHeadBranch;

        NativePath targetRepoPath = executionResult.TargetRepoPath;

        if (!codeFlowRes.HadUpdates)
        {
            _logger.LogInformation("There were no code-flow updates for subscription {subscriptionId}", subscription.Id);
            return new CodeFlowExecutionResult(codeFlowRes, unsafeFlown, prHeadBranch, targetRepoPath, null);
        }

        _logger.LogInformation("Code changes for {subscriptionId} ready in local branch {branch}",
            subscription.Id,
            prHeadBranch);

        using (var scope = _telemetryRecorder.RecordGitOperation(TrackedGitOperation.Push, subscription.TargetRepository))
        {
            await _gitClient.Push(targetRepoPath, prHeadBranch, subscription.TargetRepository);
            scope.SetSuccess();
        }

        prInfo?.HeadBranchSha = await _gitClient.GetShaForRefAsync(targetRepoPath, prHeadBranch);

        await _subscriptionEventRecorder.RegisterSubscriptionUpdateAction(SubscriptionUpdateAction.ApplyingUpdates, update.SubscriptionId);

        return new CodeFlowExecutionResult(codeFlowRes, unsafeFlown, prHeadBranch, targetRepoPath, null);
    }

    private async Task<CodeFlowExecutionResult> ExecuteWithUnsafeFallbackAsync(
            SubscriptionDTO subscription,
            BuildDTO build,
            string prHeadBranch,
            bool forceUpdate)
    {
        try
        {
            return await ExecuteFlowAttemptAsync(
                subscription,
                build,
                prHeadBranch,
                forceUpdate,
                unsafeFlow: false);
        }
        catch (NonLinearCodeflowException e)
        {
            if (e.FlowingOldBuild)
            {
                throw new SubscriptionUpdateInputException("The commit of the build being triggered is older than the already applied commit.");
            }

            string unsafeFlowBranch = GetNewBranchName(subscription.TargetBranch);

            _logger.LogInformation(
                "Unsafe {direction}-flowing build {buildId} of {sourceRepo} for subscription {subscriptionId} targeting {targetRepo} / {targetBranch} to new branch {newBranch}",
                subscription.IsForwardFlow() ? "Forward" : "Back",
                build.Id,
                subscription.SourceRepository,
                subscription.Id,
                subscription.TargetRepository,
                subscription.TargetBranch,
                unsafeFlowBranch);

            return await ExecuteFlowAttemptAsync(
                subscription,
                build,
                unsafeFlowBranch,
                forceUpdate,
                unsafeFlow: true);
        }
    }

    private async Task<CodeFlowExecutionResult> ExecuteFlowAttemptAsync(
        SubscriptionDTO subscription,
        BuildDTO build,
        string prHeadBranch,
        bool forceUpdate,
        bool unsafeFlow)
    {
        try
        {
            CodeFlowResult result = await InvokeFlowAsync(
                subscription,
                build,
                prHeadBranch,
                forceUpdate,
                unsafeFlow);
            NativePath targetRepoPath = subscription.IsForwardFlow() ? _vmrInfo.VmrPath : result.RepoPath;

            if (result.HadConflicts)
            {
                _logger.LogInformation("Detected conflicts while rebasing new changes");
                return new CodeFlowExecutionResult(
                    result,
                    unsafeFlow,
                    prHeadBranch,
                    targetRepoPath,
                    CodeFlowManualInterventionReason.Conflict);
            }

            return new CodeFlowExecutionResult(
                result,
                unsafeFlow,
                prHeadBranch,
                targetRepoPath,
                ManualInterventionReason: null);
        }
        catch (RecreationLimitReachedException e)
        {
            return new CodeFlowExecutionResult(
                CodeFlowResult: null,
                unsafeFlow,
                prHeadBranch,
                e.TargetRepoPath,
                CodeFlowManualInterventionReason.RecreationFallbackLimitReached);
        }
    }

    private async Task CreateEmptyPrBranch(
        SubscriptionDTO subscription,
        NativePath localTargetRepoPath,
        string prBranchName,
        string baseCommit,
        string initialCommitMessage)
    {
        await _gitClient.ForceCheckoutAsync(localTargetRepoPath, baseCommit);
        await _gitClient.CreateBranchAsync(localTargetRepoPath, prBranchName, overwriteExistingBranch: true);
        await _gitClient.CommitAsync(localTargetRepoPath, initialCommitMessage, allowEmpty: true);
        await _gitClient.Push(localTargetRepoPath, prBranchName, subscription.TargetRepository, force: true);
    }

    private async Task RebaseEmptyPRsOnTargetBranchAsync(
        SubscriptionDTO subscription,
        InProgressPullRequest pr)
    {
        NativePath targetRepoPath;
        if (subscription.IsForwardFlow())
        {
            targetRepoPath = (await _vmrCloneManager.PrepareVmrAsync(
                [subscription.TargetRepository],
                [pr.HeadBranch, subscription.TargetBranch],
                pr.HeadBranch,
                resetToRemote: true)).Path;
        }
        else
        {
            targetRepoPath = (await _repositoryCloneManager.PrepareCloneAsync(
                subscription.TargetRepository,
                pr.HeadBranchSha)).Path;
        }

        var initialCommitMessage = GetManualInterventionInitialCommitMessage(subscription);
        var (prIsEmpty, latestPrCommit, latestTargetBranchCommit) =
            await ManualConflictResolutionHelper.GetManualConflictResolutionPrStateAsync(
                _gitClient,
                subscription,
                targetRepoPath,
                pr.HeadBranch,
                initialCommitMessage);

        if (prIsEmpty && !await _gitClient.IsAncestorCommit(targetRepoPath, latestTargetBranchCommit, latestPrCommit))
        {
            _logger.LogInformation("Rebasing empty PR branch {headBranch} onto {targetBranch}", pr.HeadBranch, subscription.TargetBranch);
            await CreateEmptyPrBranch(subscription, targetRepoPath, pr.HeadBranch, latestTargetBranchCommit, initialCommitMessage);
        }
    }

    private async Task<CodeFlowResult> InvokeFlowAsync(
        SubscriptionDTO subscription,
        BuildDTO build,
        string branch,
        bool forceUpdate,
        bool unsafeFlow)
    {
        try
        {
            return subscription.IsForwardFlow()
                ? await _vmrForwardFlower.FlowForwardAsync(
                    subscription,
                    build,
                    branch,
                    forceUpdate,
                    unsafeFlow: unsafeFlow,
                    cancellationToken: default)
                : await _vmrBackFlower.FlowBackAsync(
                    subscription,
                    build,
                    branch,
                    forceUpdate,
                    unsafeFlow: unsafeFlow,
                    cancellationToken: default);
        }
        catch (Exception e) when (e is not NonLinearCodeflowException)
        {
            _logger.LogError("Failed to flow source changes for build {buildId} in subscription {subscriptionId}",
                build.Id,
                subscription.Id);
            throw;
        }
    }

    private static string GetManualInterventionInitialCommitMessage(SubscriptionDTO subscription)
        => $"Initial commit for subscription {subscription.Id}";

    private static string GetNewBranchName(string targetBranch) => $"darc-{targetBranch}-{Guid.NewGuid()}";
}
