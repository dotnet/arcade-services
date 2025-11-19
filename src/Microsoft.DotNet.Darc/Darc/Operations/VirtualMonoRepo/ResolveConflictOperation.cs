// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class ResolveConflictOperation(
        ResolveConflictCommandLineOptions options,
        IVmrForwardFlower forwardFlower,
        IVmrBackFlower backFlower,
        IBackflowConflictResolver backflowConflictResolver,
        IVmrInfo vmrInfo,
        IVmrCloneManager vmrCloneManager,
        IRepositoryCloneManager repositoryCloneManager,
        IVmrDependencyTracker dependencyTracker,
        IDependencyFileManager dependencyFileManager,
        ILocalGitRepoFactory localGitRepoFactory,
        IBarApiClient barApiClient,
        IFileSystem fileSystem,
        IProcessManager processManager,
        ILogger<ResolveConflictOperation> logger)
    : CodeFlowOperation(options, forwardFlower, backFlower, backflowConflictResolver, vmrInfo, vmrCloneManager, dependencyTracker, dependencyFileManager, localGitRepoFactory, barApiClient, fileSystem, logger)
{
    private readonly ResolveConflictCommandLineOptions _options = options;
    private readonly IVmrInfo _vmrInfo = vmrInfo;
    private readonly IVmrCloneManager _vmrCloneManager = vmrCloneManager;
    private readonly IRepositoryCloneManager _repositoryCloneManager = repositoryCloneManager;
    private readonly IProcessManager _processManager = processManager;
    private readonly IBarApiClient _barClient = barApiClient;
    private readonly ILogger<ResolveConflictOperation> _logger = logger;

    private const string ResolveConflictCommitMessage =
        $$"""
        [{name}] Source update {oldShaShort}{{DarcLib.Constants.Arrow}}{newShaShort}
        Diff: {remote}/compare/{oldSha}..{newSha}
        
        From: {remote}/commit/{oldSha}
        To: {remote}/commit/{newSha}

        The following files had conflicts that were resolved by a user:

        {conflictingFilesList}

        {{DarcLib.Constants.AUTOMATION_COMMIT_TAG}}
        """;

    protected override async Task ExecuteInternalAsync(
        string repoName,
        string? sourceDirectory,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        CancellationToken cancellationToken)
    {
        NativePath targetGitRepoPath = new(_processManager.FindGitRoot(Directory.GetCurrentDirectory()));

        _logger.LogInformation("Fetching subscription {subscriptionId}...", _options.SubscriptionId);
        var subscription = await FetchCodeflowSubscriptionAsync(_options.SubscriptionId);

        _logger.LogInformation("Fetching PR information...");
        TrackedPullRequest pr = await _barClient.GetTrackedPullRequestBySubscriptionIdAsync(subscription.Id)
            ?? throw new DarcException($"No open PR found for this subscription");
        _logger.LogInformation("Found open PR: {prId}", pr.Url);

        _logger.LogInformation("Fetching build to apply...");
        var build = await _barClient.GetBuildAsync(pr.Updates.Last().BuildId);
        _logger.LogInformation("Build {buildId} / {buildName} found: {buildUrl}", build.Id, build.AzureDevOpsBuildNumber, build.GetBuildLink());

        NativePath repoPath;
        ILocalGitRepo vmr;

        _logger.LogInformation("Fetching PR branch {branchName}...", pr.HeadBranch);

        if (subscription.IsForwardFlow())
        {
            // Register/prepare VMR on the current directory
            vmr = await _vmrCloneManager.PrepareVmrAsync(
                targetGitRepoPath,
                [subscription.TargetRepository],
                [pr.HeadBranch],
                pr.HeadBranch,
                resetToRemote: false,
                cancellationToken);

            _logger.LogInformation("Cloning source repository {repoUrl} at commit {commit}...",
                build.GetRepository(),
                DarcLib.Commit.GetShortSha(build.Commit));

            // Clone the source repo to a temp location
            repoPath = (await _repositoryCloneManager.PrepareCloneAsync(
                build.GetRepository(),
                build.Commit,
                cancellationToken: cancellationToken)).Path;
        }
        else
        {
            // Register/prepare target repo on the current directory
            repoPath = targetGitRepoPath;
            await _repositoryCloneManager.PrepareCloneAsync(
                targetGitRepoPath,
                [subscription.TargetRepository],
                [pr.HeadBranch],
                pr.HeadBranch,
                resetToRemote: false,
                cancellationToken);

            _logger.LogInformation("Cloning VMR {repoUrl} at commit {commit}...",
                build.GetRepository(),
                DarcLib.Commit.GetShortSha(build.Commit));

            // Clone VMR to a temp location
            vmr = await _vmrCloneManager.PrepareVmrAsync(
                [build.GetRepository()],
                [build.Commit],
                build.Commit,
                resetToRemote: false,
                cancellationToken);
        }

        string lastFlownSha = await GetLastFlownShaAsync(subscription, repoPath);
        _vmrInfo.VmrPath = vmr.Path;

        await ValidateLocalRepo(subscription, repoPath);

        try
        {
            await FlowCodeLocallyAsync(
                repoPath,
                isForwardFlow: subscription.IsForwardFlow(),
                additionalRemotes,
                build,
                subscription,
                cancellationToken);
        }
        catch (PatchApplicationLeftConflictsException e)
        when (e.ConflictedFiles != null && e.ConflictedFiles.Count != 0)
        {
            _logger.LogInformation("Codeflow has finished, and {conflictedFiles} conflicting file(s) have been" +
                " left on the current branch.", e.ConflictedFiles.Count);
            _logger.LogInformation("Please resolve the conflicts locally, commit and push your changes to unblock the codeflow PR.");

            CreateCommitMessageFile(targetGitRepoPath, subscription, build, lastFlownSha, e.ConflictedFiles);

            return;
        }

        _logger.LogInformation("Codeflow has finished and changes have been staged on the local branch. "
            + "However, no conflicts were encountered.");
    }

    private async Task ValidateLocalRepo(Subscription subscription, NativePath repoPath)
    {
        var mappingName = subscription.IsForwardFlow()
            ? subscription.TargetDirectory
            : subscription.SourceDirectory;

        var local = new Local(_options.GetRemoteTokenProvider(), _logger, repoPath);
        var sourceDependency = await local.GetSourceDependencyAsync();

        if (string.IsNullOrEmpty(sourceDependency?.Mapping))
        {
            throw new DarcException("The current working directory does not appear to be a repository managed by darc.");
        }

        if (!sourceDependency.Mapping.Equals(mappingName))
        {
            throw new DarcException("The current working directory does not match the subscription " +
                $"source directory '{subscription.SourceDirectory}'.");
        }
    }

    private async Task<Subscription> FetchCodeflowSubscriptionAsync(string subscriptionId)
    {
        if (string.IsNullOrEmpty(subscriptionId))
        {
            throw new ArgumentException("Please specify a subscription id.");
        }

        var subscription = await _barClient.GetSubscriptionAsync(subscriptionId)
            ?? throw new DarcException($"No subscription found with id `{subscriptionId}`.");

        if (!subscription.SourceEnabled)
        {
            throw new DarcException($"Subscription with id `{subscription.Id}` is not a codeflow subscription.");
        }

        return subscription;
    }

    private async Task<string> GetLastFlownShaAsync(Subscription subscription, NativePath repoPath)
    {
        var local = new Local(_options.GetRemoteTokenProvider(), _logger, repoPath);

        if (subscription.IsForwardFlow())
        {
            var sourceManifest = await local.GetSourceManifestAsync(_vmrInfo.VmrPath);
            return sourceManifest.GetRepoVersion(subscription.TargetDirectory).CommitSha;
        }
        else
        {
            var sourceDependency = await local.GetSourceDependencyAsync();
            return sourceDependency.Sha;
        }
    }

    private void CreateCommitMessageFile(
        string targetRepoPath,
        Subscription subscription,
        Build build,
        string lastFlownSha,
        IEnumerable<UnixPath> conflictedFiles)
    {
        var commitEditMsgPath = Path.Combine(targetRepoPath, ".git", "SQUASH_MSG");

        var mappingName = subscription.IsForwardFlow()
            ? subscription.TargetDirectory
            : subscription.SourceDirectory;

        var conflictedFilesList = string.Join(
            Environment.NewLine,
            conflictedFiles.Select(f => $"- {f}"));

        var commitMessage = VmrManagerBase.PrepareCommitMessage(
            ResolveConflictCommitMessage,
            mappingName,
            subscription.SourceRepository,
            DarcLib.Commit.GetShortSha(lastFlownSha),
            DarcLib.Commit.GetShortSha(build.Commit),
            conflictingFiles: conflictedFilesList);

        File.WriteAllText(commitEditMsgPath, commitMessage);
    }
}
