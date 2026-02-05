// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Reflection;
using System.Text;
using Maestro.Common;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;
using ProductConstructionService.DependencyFlow.Model;
using ProductConstructionService.DependencyFlow.WorkItems;

namespace ProductConstructionService.DependencyFlow;

public interface IPullRequestCommentBuilder
{
    Task<string?> BuildTagSourceRepositoryGitHubContactsCommentAsync(InProgressPullRequest pr);
}

public class PullRequestCommentBuilder : IPullRequestCommentBuilder
{
    private readonly ILogger<IPullRequestCommentBuilder> _logger;
    private readonly IRemoteFactory _remoteFactory;
    private readonly IBasicBarClient _barClient;

    public PullRequestCommentBuilder(
        ILogger<IPullRequestCommentBuilder> logger,
        IRemoteFactory remoteFactory,
        IBasicBarClient barClient)
    {
        _logger = logger;
        _remoteFactory = remoteFactory;
        _barClient = barClient;
    }

    internal static string BuildNotificationAboutManualConflictResolutionComment(
        SubscriptionUpdateWorkItem update,
        Subscription subscription,
        IReadOnlyCollection<UnixPath> conflictedFiles,
        string prHeadBranch,
        bool prIsEmpty)
    {
        var comment = new StringBuilder()
            .Append(prIsEmpty ? "# :rotating_light: Action Required" : "# :stop_sign: Codeflow Paused")
            .AppendLine(" — Conflict detected")
            .Append($"A conflict was detected when trying to update this PR with changes from build `{update.BuildId}` of ")
            .Append(GitRepoUrlUtils.GetRepoAtCommitUri(update.SourceRepo, update.SourceSha))
            .AppendLine(".");

        if (!prIsEmpty)
        {
            comment
                .AppendLine()
                .Append("**:bulb: You can either merge the PR without getting these new updates ")
                .AppendLine("or manually flow them in and resolve the conflicts so that automated codeflow can resume for this PR.**");
        }

        comment
            .AppendLine();

        var notificationTags = GetNotificationTags(subscription);
        if (!string.IsNullOrEmpty(notificationTags))
        {
            comment
                .Append(notificationTags)
                .AppendLine(" please help resolve the conflict in this PR.")
                .AppendLine();
        }

        comment
            .AppendLine("The conflicts in the following files need to be manually resolved:")
            .AppendConflictedFileList(update, subscription, conflictedFiles, prHeadBranch)
            .AppendLine();

        // https://github.com/dotnet/arcade-services/issues/5443 - temporary hardcode the version to give repos time to get new arcade
        var maestroVersion = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location)
            .ProductVersion!
            .Split('+')
            .First();

        comment
            .AppendLine(
            $"""
            #### :information_source: To resolve the conflicts, please follow these steps:
            1. Clone the current repository
                ```bash
                git clone {subscription.TargetRepository}
                cd {subscription.TargetRepository.Split('/', StringSplitOptions.RemoveEmptyEntries).Last()}
                ```
            2. Make sure your `darc` is [up-to-date](https://github.com/dotnet/arcade-services/blob/main/docs/Darc.md#setting-up-your-darc-client)
                *(version {maestroVersion} or higher)*
                ```bash
                # Linux / MacOS
                ./eng/common/darc-init.sh
                # or on Windows
                .\eng\common\darc-init.ps1
                ```
            3. Run from repo's git clone and follow the instructions provided by the command to resolve the conflict locally
                ```bash
                darc vmr resolve-conflict --subscription {subscription.Id}
                ```
                This should apply the build `{update.BuildId}` with sources from [`{Microsoft.DotNet.DarcLib.Commit.GetShortSha(update.SourceSha)}`]({GitRepoUrlUtils.GetRepoAtCommitUri(update.SourceRepo, update.SourceSha)})
            4. Resolve the conflicts, commit & push the changes
            5. Once pushed, the `Codeflow verification` check will turn green.  
                If not, a new build might have flown into the PR and you might need to run the command above again.
            """);

        return comment.ToString();
    }

    public static string BuildOppositeCodeflowMergedNotification() =>
        """
        While this PR was open, the source repository has received code changes from this repository (an opposite codeflow merged).
        To avoid complex conflicts, the codeflow cannot continue until this PR is closed or merged.
        
        You can continue with one of the following options:
        - Ignore this and merge this PR as usual without waiting for the new changes.
          Once merged, Maestro will create a new codeflow PR with the new changes.
        - Close this PR and wait for Maestro to open a new one with old and new changes included.
          You will lose any manual changes made in this PR.
          You can also manually trigger the new codeflow right away by running:
          ```
          darc trigger-subscriptions --id <subscriptionId>
          ```
        - Force a codeflow into this PR at your own risk if you want the new changes.
          User commits made to this PR might be reverted.
          ```
          darc trigger-subscriptions --id <subscriptionId> --force
          ```
        """;

    public async Task<string?> BuildTagSourceRepositoryGitHubContactsCommentAsync(InProgressPullRequest pr)
    {
        // We'll try to notify the source repo if the subscription provided a list of aliases to tag.
        // The API checks when creating / updating subscriptions that any resolve-able logins are in the
        // "Microsoft" Github org, so we can safely use them in any comment.
        if (pr.SourceRepoNotified == true)
        {
            _logger.LogInformation("Skipped notifying source repository for {url}'s failed policies, as it has already been tagged", pr.Url);
            return null;
        }

        var subscriptionFromPr = pr.ContainedSubscriptions.FirstOrDefault();
        if (subscriptionFromPr == null)
        {
            _logger.LogWarning("Unable to get any contained subscriptions from this PR for notification; skipping attempts to notify.");
            pr.SourceRepoNotified = true;
            return string.Empty;
        }

        // In practice these all contain the same subscription id, the property is more like "containedBuildsAndTheirSubscriptions"
        _logger.LogInformation("PR contains {count} builds.  Using first ({subscription}) for notification tagging.",
            pr.ContainedSubscriptions.Count,
            subscriptionFromPr.SubscriptionId);

        (var owner, var repo, var prIssueId) = GitHubClient.ParsePullRequestUri(pr.Url);
        if (owner == null || repo == null || prIssueId == 0)
        {
            _logger.LogInformation("Unable to parse pull request '{url}' (typically due to Azure DevOps pull requests), will not notify on this PR.", pr.Url);
            pr.SourceRepoNotified = true;
            return string.Empty;
        }

        var darcRemote = await _remoteFactory.CreateRemoteAsync($"https://github.com/{owner}/{repo}");
        var subscription = await _barClient.GetSubscriptionAsync(subscriptionFromPr.SubscriptionId);
        var sourceRepository = subscription.SourceRepository;
        var targetRepository = subscription.TargetRepository;

        // If we're here, there are failing checks, but if the only checks that failed were Maestro Merge Policy checks, we'll skip informing until something else fails too.
        var prChecks = await darcRemote.GetPullRequestChecksAsync(pr.Url);
        var failedPrChecks = prChecks.Where(p => !p.IsMaestroMergePolicy && (p.Status == CheckState.Failure || p.Status == CheckState.Error)).AsEnumerable();
        if (!failedPrChecks.Any())
        {
            _logger.LogInformation("All failing or error state checks are 'Maestro Merge Policy'-type checks, not notifying subscribed users.");
            return string.Empty;
        }

        var tagsToNotify = GetNotificationTags(subscription);
        if (string.IsNullOrEmpty(tagsToNotify))
        {
            _logger.LogInformation("Found no matching tags for source '{sourceRepo}' to target '{targetRepo}' on channel '{channel}'. ", sourceRepository, targetRepository, subscription.Channel);
            return string.Empty;
        }

        var sourceRepoNotificationComment = $"""
            #### Notification for subscribed users from {sourceRepository}:

            {tagsToNotify}

            #### Action requested: Please take a look at this failing automated dependency-flow pull request's checks; failures may be related to changes which originated in the source repo.

            - This pull request contains changes from {sourceRepository} and failed checks in this PR. Please take a peek at the failures and/or push a fix.
            - If you're being tagged in this comment it is due to an entry in the related [Maestro Subscription](https://maestro.dot.net/subscriptions?search={subscription.Id}&showDisabled=True).
            - For more details, please read [the Darc documentation](https://github.com/dotnet/arcade-services/blob/main/docs/Darc.md#update-subscription).
            """;

        pr.SourceRepoNotified = true;
        return sourceRepoNotificationComment;
    }

    private static string GetNotificationTags(Subscription subscription)
    {
        var tagsToNotify = (subscription.PullRequestFailureNotificationTags ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.StartsWith('@') ? t : $"@{t}");

        return string.Join(Environment.NewLine, tagsToNotify);
    }
}

static file class StringBuilderExtensions
{
    public static StringBuilder AppendConflictedFileList(
        this StringBuilder sb,
        SubscriptionUpdateWorkItem update,
        Subscription subscription,
        IReadOnlyCollection<UnixPath> conflictedFiles,
        string prHeadBranch)
    {
        string srcDir = subscription.IsForwardFlow()
            ? VmrInfo.GetRelativeRepoSourcesPath(subscription.TargetDirectory)
            : string.Empty;

        foreach (UnixPath filePath in conflictedFiles)
        {
            string relativeFilePath = filePath.ToString();

            if (subscription.IsForwardFlow())
            {
                // For forward flow, only include files under the repository source directory
                if (!relativeFilePath.StartsWith(srcDir + "/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                relativeFilePath = relativeFilePath.Substring(srcDir.Length + 1);
            }

            var (fileUrlInVmr, fileUrlInRepo) = GetFileUrls(update, subscription, relativeFilePath, prHeadBranch);
            string vmrLink = $"[VMR]({fileUrlInVmr})";
            string repoLink = $"[{GitRepoUrlUtils.GetRepoNameWithOrg(subscription.IsBackflow() ? subscription.TargetRepository : subscription.SourceRepository)}]({fileUrlInRepo})";
            sb.AppendLine($" - `{relativeFilePath}`");
            sb.AppendLine($"     *🔍 View file in {repoLink} vs {vmrLink}*");
        }

        return sb;
    }

    private static (string, string) GetFileUrls(SubscriptionUpdateWorkItem update,
        Subscription subscription,
        string filePath,
        string prHeadBranch)
    {
        string vmrFileUrl;
        string repoFileUrl;
        if (subscription.IsBackflow())
        {
            vmrFileUrl = GitRepoUrlUtils.GetVmrFileAtCommitUri(update.SourceRepo, subscription.SourceDirectory, update.SourceSha, filePath);
            repoFileUrl = GitRepoUrlUtils.GetRepoFileAtBranchUri(subscription.TargetRepository, prHeadBranch, filePath);
        }
        else if (subscription.IsForwardFlow())
        {
            vmrFileUrl = GitRepoUrlUtils.GetVmrFileAtBranchUri(subscription.TargetRepository, subscription.TargetDirectory, prHeadBranch, filePath);
            repoFileUrl = GitRepoUrlUtils.GetRepoFileAtCommitUri(update.SourceRepo, update.SourceSha, filePath);
        }
        else
        {
            throw new InvalidOperationException($"Failed to generate codeflow conflict message because subscription {subscription.Id} is not source-enabled.");
        }
        return (vmrFileUrl, repoFileUrl);
    }
}
