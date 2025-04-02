// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using System.Text.Json;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;
using ProductConstructionService.DependencyFlow.WorkItems;

namespace ProductConstructionService.DependencyFlow;

internal interface IPullRequestConflictNotifier
{
    /// <summary>
    /// Posts a comment in the PR when a new build/update cannot be flown to it
    /// because there are conflicts between the sources in the PR and in the update.
    /// </summary>
    Task NotifyAboutConflictingUpdateAsync(
        ConflictInPrBranchException conflictException,
        SubscriptionUpdateWorkItem update,
        Subscription subscription,
        InProgressPullRequest pr);

    /// <summary>
    /// Posts a comment in the PR when the PR has a conflict with the target branch.
    /// This means, there might be conflicts in the version files and the comment will
    /// provide guidance on how to resolve those.
    /// </summary>
    Task NotifyAboutMergeConflictAsync(
        InProgressPullRequest pr,
        SubscriptionUpdateWorkItem update,
        Subscription subscription,
        IReadOnlyCollection<UnixPath> conflictedFiles);
}

internal class PullRequestConflictNotifier : IPullRequestConflictNotifier
{
    private readonly IRemoteFactory _remoteFactory;
    private readonly ILogger<PullRequestConflictNotifier> _logger;

    public PullRequestConflictNotifier(
        IRemoteFactory remoteFactory,
        ILogger<PullRequestConflictNotifier> logger)
    {
        _remoteFactory = remoteFactory;
        _logger = logger;
    }

    /// <summary>
    /// Handles a case when new code flow updates cannot be flowed into an existing PR,
    /// because the PR contains a change conflicting with the new updates.
    /// In this case, we post a comment on the PR with the list of files that are in conflict,
    /// </summary>
    public async Task NotifyAboutConflictingUpdateAsync(
        ConflictInPrBranchException conflictException,
        SubscriptionUpdateWorkItem update,
        Subscription subscription,
        InProgressPullRequest pr)
    {
        // The PR we're trying to update has a conflict with the source repo. We will mark it as blocked, not allowing any updates from this
        // subscription till it's merged, or the conflict resolved. We'll set a reminder to check on it.
        StringBuilder sb = new();
        sb.AppendLine($"There was a conflict in the PR branch when flowing source from {update.GetRepoAtCommitUri()}");
        sb.AppendLine("Files conflicting with the head branch:");
        foreach (var file in conflictException.FilesInConflict)
        {
            sb.AppendLine($" - [{file}]({update.GetFileAtCommitUri(file)})");
        }
        sb.AppendLine();
        sb.AppendLine("Updates from this subscription will be blocked until the conflict is resolved, or the PR is merged");

        var remote = await _remoteFactory.CreateRemoteAsync(subscription.TargetRepository);

        try
        {
            await remote.CommentPullRequestAsync(pr.Url, sb.ToString());
        }
        catch (Exception e)
        {
            _logger.LogWarning("Posting comment to {prUrl} failed with exception {message}", pr.Url, e.Message);
        }
    }

    /// <summary>
    /// Handles a case when new code flow updates cannot be flowed into an existing PR,
    /// because the PR contains a change conflicting with the new updates.
    /// In this case, we post a comment on the PR with the list of files that are in conflict,
    /// </summary>
    public async Task NotifyAboutMergeConflictAsync(
        InProgressPullRequest pr,
        SubscriptionUpdateWorkItem update,
        Subscription subscription,
        IReadOnlyCollection<UnixPath> conflictedFiles)
    {
        string metadataFile, contentType, correctContent;

        if (subscription.IsBackflow()!.Value)
        {
            if (!conflictedFiles.Any(f => f.Path.Equals(VersionFiles.VersionDetailsXml, StringComparison.InvariantCultureIgnoreCase)))
            {
                // No version files in conflict, so we don't need to post a comment
                return;
            }

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
            if (!conflictedFiles.Any(f => f.Path.Equals(VmrInfo.DefaultRelativeSourceManifestPath.Path, StringComparison.InvariantCultureIgnoreCase)
                || f.Path.StartsWith(VmrInfo.GitInfoSourcesDir)))
            {
                // No version files in conflict, so we don't need to post a comment
                return;
            }

            metadataFile = VmrInfo.DefaultRelativeSourceManifestPath;
            contentType = "json";
            correctContent = JsonSerializer.Serialize(
                new RepositoryRecord(
                    subscription.TargetDirectory,
                    update.SourceRepo,
                    update.SourceSha,
                    update.Assets.FirstOrDefault()?.Version,
                    update.BuildId),
                new JsonSerializerOptions()
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                });
        }

        // Make the XML/JSON part of the quoted block correctly
        correctContent = correctContent.Replace("\n", "\n> ");

        string comment =
            $"""
            > [!IMPORTANT]
            > There are conflicts with the `{subscription.TargetBranch}` branch in this PR. Apart from conflicts in the source files, this means there are unresolved conflicts in the codeflow metadata file `{metadataFile}`.
            > When resolving these, please use the (incoming/ours) version from the PR branch. The correct content should be this:
            > ```{contentType}
            > {correctContent}
            > ```
            > 
            > In case of unclarities, consult the [FAQ]({PullRequestBuilder.CodeFlowPrFaqUri}) or tag **\@dotnet/product-construction** for assistance.
            """;

        var remote = await _remoteFactory.CreateRemoteAsync(subscription.TargetRepository);

        try
        {
            await remote.CommentPullRequestAsync(pr.Url, comment);
        }
        catch (Exception e)
        {
            _logger.LogWarning("Posting comment to {prUrl} failed with exception {message}", pr.Url, e.Message);
        }
    }
}
