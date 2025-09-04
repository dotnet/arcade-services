// Licensed to the .NET Foundation under one or more agreements.
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
using ProductConstructionService.DependencyFlow.WorkItems;

namespace ProductConstructionService.DependencyFlow;

public interface IPullRequestCommentBuilder
{
    Task<string> BuildTagSourceRepositoryGitHubContactsCommentAsync(InProgressPullRequest pr);
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
        List<string> filesInConflict,
        SubscriptionUpdateWorkItem update,
        Subscription subscription,
        InProgressPullRequest pr,
        string prHeadBranch)
    {
        StringBuilder sb = new();
        sb.AppendLine($"There was a conflict in the PR branch when flowing source from {GitRepoUrlUtils.GetRepoAtCommitUri(update.SourceRepo, update.SourceSha)}");
        sb.AppendLine("Files conflicting with the head branch:");
        foreach (var filePath in filesInConflict)
        {
            var (fileUrlInVmr, fileUrlInRepo) = GetFileUrls(update, subscription, filePath, prHeadBranch);
            string vmrString = $"[🔍 View in VMR]({fileUrlInVmr})";
            string repoString = $"[🔍 View in {GitRepoUrlUtils.GetRepoNameWithOrg(subscription.IsBackflow() ? subscription.TargetRepository : subscription.SourceRepository)}]({fileUrlInRepo})";
            sb.AppendLine($" - `{filePath}` - {repoString} / {vmrString}");
        }
        sb.AppendLine();
        sb.AppendLine("Updates from this subscription will be blocked until the conflict is resolved, or the PR is merged");

        return sb.ToString();
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
                There are conflicts with the `{subscription.TargetBranch}` branch in this PR. Apart from conflicts in the source files, this means there are unresolved conflicts in the codeflow metadata file `{metadataFile}`.
                When resolving these, please use the (incoming/ours) version from the PR branch. The correct content should be this:
                ```{contentType}
                {correctContent}
                ```
                """;

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

    public async Task<string> BuildTagSourceRepositoryGitHubContactsCommentAsync(InProgressPullRequest pr)
    {
        // We'll try to notify the source repo if the subscription provided a list of aliases to tag.
        // The API checks when creating / updating subscriptions that any resolve-able logins are in the
        // "Microsoft" Github org, so we can safely use them in any comment.
        if (pr.SourceRepoNotified == true)
        {
            _logger.LogInformation("Skipped notifying source repository for {url}'s failed policies, as it has already been tagged", pr.Url);
            return string.Empty;
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
        var darcSubscriptionObject = await _barClient.GetSubscriptionAsync(subscriptionFromPr.SubscriptionId);
        var sourceRepository = darcSubscriptionObject.SourceRepository;
        var targetRepository = darcSubscriptionObject.TargetRepository;

        // If we're here, there are failing checks, but if the only checks that failed were Maestro Merge Policy checks, we'll skip informing until something else fails too.
        var prChecks = await darcRemote.GetPullRequestChecksAsync(pr.Url);
        var failedPrChecks = prChecks.Where(p => !p.IsMaestroMergePolicy && (p.Status == CheckState.Failure || p.Status == CheckState.Error)).AsEnumerable();
        if (!failedPrChecks.Any())
        {
            _logger.LogInformation("All failing or error state checks are 'Maestro Merge Policy'-type checks, not notifying subscribed users.");
            return string.Empty;
        }

        var tagsToNotify = new List<string>();
        if (!string.IsNullOrEmpty(darcSubscriptionObject.PullRequestFailureNotificationTags))
        {
            tagsToNotify.AddRange(darcSubscriptionObject.PullRequestFailureNotificationTags.Split(';', StringSplitOptions.RemoveEmptyEntries));
        }

        if (tagsToNotify.Count == 0)
        {
            _logger.LogInformation("Found no matching tags for source '{sourceRepo}' to target '{targetRepo}' on channel '{channel}'. ", sourceRepository, targetRepository, darcSubscriptionObject.Channel);
            return string.Empty;
        }

        // At this point we definitely have notifications to make, so do it.
        _logger.LogInformation("Found {count} matching tags for source '{sourceRepo}' to target '{targetRepo}' on channel '{channel}'. ", tagsToNotify.Count, sourceRepository, targetRepository, darcSubscriptionObject.Channel);

        // To ensure GitHub notifies the people / teams on the list, forcibly check they are inserted with a preceding '@'
        for (var i = 0; i < tagsToNotify.Count; i++)
        {
            if (!tagsToNotify[i].StartsWith('@'))
            {
                tagsToNotify[i] = $"@{tagsToNotify[i]}";
            }
        }

        var sourceRepoNotificationComment = $"""
            #### Notification for subscribed users from {sourceRepository}:

            {string.Join($", {Environment.NewLine}", tagsToNotify)}

            #### Action requested: Please take a look at this failing automated dependency-flow pull request's checks; failures may be related to changes which originated in your repo.

            - This pull request contains changes from your source repo ({sourceRepository}) and seems to have failed checks in this PR.  Please take a peek at the failures and comment if they seem relevant to your changes.
            - If you're being tagged in this comment it is due to an entry in the related Maestro Subscription of the Build Asset Registry.  If you feel this entry has added your GitHub login or your GitHub team in error, please update the subscription to reflect this.
            - For more details, please read [the Arcade Darc documentation](https://github.com/dotnet/arcade/blob/main/Documentation/Darc.md#update-subscription)
            """;

        pr.SourceRepoNotified = true;
        return sourceRepoNotificationComment;
    }
}
