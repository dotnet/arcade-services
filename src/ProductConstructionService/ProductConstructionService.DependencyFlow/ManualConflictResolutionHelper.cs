// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging;
using ProductConstructionService.DependencyFlow.Model;

using SubscriptionDTO = Microsoft.DotNet.ProductConstructionService.Client.Models.Subscription;

namespace ProductConstructionService.DependencyFlow;

internal static class ManualConflictResolutionHelper
{
    public static async Task RebaseEmptyPRsOnTargetBranchAsync(
        ILocalLibGit2Client gitClient,
        ILogger<VmrCodeFlower> logger,
        SubscriptionDTO subscription,
        InProgressPullRequest pr,
        NativePath targetRepoPath)
    {
        string initialCommitMessage = GetManualConflictResolutionInitialCommitMessage(subscription);
        var (prIsEmpty, latestPrCommit, latestTargetBranchCommit) =
            await GetManualConflictResolutionPrStateAsync(
                gitClient,
                subscription,
                targetRepoPath,
                pr.HeadBranch,
                initialCommitMessage);

        if (prIsEmpty && !await gitClient.IsAncestorCommit(targetRepoPath, latestTargetBranchCommit, latestPrCommit))
        {
            logger.LogInformation(
                "Rebasing empty PR branch {headBranch} onto {targetBranch}",
                pr.HeadBranch,
                subscription.TargetBranch);
            await gitClient.ForceCheckoutAsync(targetRepoPath, latestTargetBranchCommit);
            await gitClient.CreateBranchAsync(targetRepoPath, pr.HeadBranch, overwriteExistingBranch: true);
            await gitClient.CommitAsync(targetRepoPath, initialCommitMessage, allowEmpty: true);
            await gitClient.Push(targetRepoPath, pr.HeadBranch, subscription.TargetRepository, force: true);
        }
    }

    public static async Task<(bool prIsEmpty, string latestPrCommit, string latestTargetBranchCommit)>
        GetManualConflictResolutionPrStateAsync(
            ILocalLibGit2Client gitClient,
            SubscriptionDTO subscription,
            NativePath localTargetRepoPath,
            string prHeadBranch,
            string initialCommitMessage)
    {
        var remoteName = (await gitClient.GetRemotesAsync(localTargetRepoPath))
            .First(r => r.Uri.Equals(subscription.TargetRepository, StringComparison.OrdinalIgnoreCase))
            .Name;
        await gitClient.UpdateRemoteAsync(localTargetRepoPath, remoteName);

        var latestPrCommit = await gitClient.GetShaForRefAsync(localTargetRepoPath, $"{remoteName}/{prHeadBranch}");
        var latestTargetBranchCommit = await gitClient.GetShaForRefAsync(
            localTargetRepoPath,
            $"{remoteName}/{subscription.TargetBranch}");
        var latestCommitMessage = await gitClient.RunGitCommandAsync(
            localTargetRepoPath,
            ["log", "-1", "--pretty=%B", latestPrCommit]);

        return (
            latestCommitMessage.StandardOutput.Trim().StartsWith(initialCommitMessage),
            latestPrCommit,
            latestTargetBranchCommit);
    }

    private static string GetManualConflictResolutionInitialCommitMessage(SubscriptionDTO subscription)
        => $"Initial commit for subscription {subscription.Id}";
}
