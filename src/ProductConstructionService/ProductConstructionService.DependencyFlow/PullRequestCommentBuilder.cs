﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using System.Text.Json;
using Maestro.Common;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
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

    public static string BuildNotifyAboutConflictingUpdateComment(
        IReadOnlyCollection<string> filesInConflict,
        SubscriptionUpdateWorkItem update,
        Subscription subscription,
        InProgressPullRequest pr,
        string prHeadBranch)
    {
        StringBuilder comment = new();
        comment.AppendLine($"There was a conflict in the PR branch when flowing source from {GitRepoUrlUtils.GetRepoAtCommitUri(update.SourceRepo, update.SourceSha)}");
        comment.AppendLine("Files conflicting with the head branch:");
        AppendConflictedFileList(update, subscription, [..filesInConflict.Select(f => new UnixPath(f))], prHeadBranch, comment);
        comment.AppendLine();
        comment.AppendLine("Updates from this subscription will be blocked until the conflict is resolved, or the PR is merged");

        var notificationTags = GetNotificationTags(subscription);
        if (!string.IsNullOrEmpty(notificationTags))
        {
            comment.AppendLine();
            comment.AppendLine("Tagging the following users to help with conflict resolution:");
            comment.AppendLine(notificationTags);
        }

        return comment.ToString();
    }

    public static string NotifyAboutMergeConflict(
        InProgressPullRequest pr,
        SubscriptionUpdateWorkItem update,
        Subscription subscription,
        IReadOnlyCollection<UnixPath> conflictedFiles,
        Build build)
    {
        string metadataFile, contentType, correctContent;

        if (subscription.IsBackflow())
        {
            metadataFile = VersionFiles.VersionDetailsXml;
            contentType = "xml";
            var sourceMetadata = new SourceDependency(
                update.SourceRepo,
                subscription.SourceDirectory,
                update.SourceSha,
                update.BuildId);
            correctContent = VersionDetailsParser.SerializeSourceDependency(sourceMetadata);
        }
        else
        {
            metadataFile = VmrInfo.DefaultRelativeSourceManifestPath;
            contentType = "json";
            correctContent = JsonSerializer.Serialize(
                new RepositoryRecord(
                    subscription.TargetDirectory,
                    update.SourceRepo,
                    update.SourceSha,
                    update.BuildId),
                new JsonSerializerOptions()
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                });
        }

        return $"""
                There are conflicts with the `{subscription.TargetBranch}` branch in this PR.
                Apart from conflicts in the source files, this means there are unresolved conflicts in the codeflow metadata file `{metadataFile}`.
                When resolving these, please use the (incoming/ours) version from the PR branch. The correct content should be this:
                ```{contentType}
                {correctContent}
                ```
                """;
    }

    internal static string BuildNotificationAboutManualConflictResolutionComment(
        SubscriptionUpdateWorkItem update,
        Subscription subscription,
        IReadOnlyCollection<UnixPath> conflictedFiles,
        string prHeadBranch)
    {
        StringBuilder comment = new();
        comment.Append($"A conflict was detected when trying to update this PR with changes from ");
        comment.Append(GitRepoUrlUtils.GetRepoAtCommitUri(update.SourceRepo, update.SourceSha));
        comment.AppendLine(".");
        comment.AppendLine();

        var notificationTags = GetNotificationTags(subscription);
        if (!string.IsNullOrEmpty(notificationTags))
        {
            comment.Append(notificationTags);
            comment.AppendLine(" please help resolve the conflict in this PR.");
            comment.AppendLine();
        }

        comment.AppendLine("The conflicts in the following files need to be manually resolved so that automated codeflow can resume for this PR:");

        AppendConflictedFileList(update, subscription, conflictedFiles, prHeadBranch, comment);

        comment.AppendLine();
        comment.AppendLine(
            $"""
            #### ℹ️ To resolve the conflict, please follow these steps:
            1. Clone the current repository
                ```bash
                git clone {subscription.TargetRepository}
                ```
            2. `cd` to the cloned repository
            3. Make sure your `darc` is [up-to-date](https://github.com/dotnet/arcade-services/blob/main/docs/Darc.md#setting-up-your-darc-client) and run
                ```bash
                darc vmr resolve-conflict --subscription {subscription.Id}
                ```
            4. Follow the instructions provided by the command to resolve the conflict and push the update
            5. This should apply the build `{update.BuildId}` with sources from `{update.SourceSha}`
            6. Once the changes are pushed, the `Codeflow verification` check will turn green
            """);

        return comment.ToString();
    }

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

    private static void AppendConflictedFileList(
        SubscriptionUpdateWorkItem update,
        Subscription subscription,
        IReadOnlyCollection<UnixPath> conflictedFiles,
        string prHeadBranch,
        StringBuilder sb)
    {
        string srcDir = subscription.IsForwardFlow()
            ? VmrInfo.GetRelativeRepoSourcesPath(subscription.TargetDirectory)
            : string.Empty;

        foreach (UnixPath filePath in conflictedFiles)
        {
            string relativeFilePath = filePath.ToString();
            if (subscription.IsForwardFlow())
            {
                relativeFilePath = relativeFilePath.Length > srcDir.Length + 1 ? relativeFilePath.Substring(srcDir.Length + 1) : relativeFilePath;
            }

            var (fileUrlInVmr, fileUrlInRepo) = GetFileUrls(update, subscription, relativeFilePath, prHeadBranch);
            string vmrLink = $"[VMR]({fileUrlInVmr})";
            string repoLink = $"[{GitRepoUrlUtils.GetRepoNameWithOrg(subscription.IsBackflow() ? subscription.TargetRepository : subscription.SourceRepository)}]({fileUrlInRepo})";
            sb.AppendLine($" - `{relativeFilePath}`");
            sb.AppendLine($"     *🔍 View file in {repoLink} vs {vmrLink}*");
        }
    }
}
