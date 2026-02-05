// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Maestro.Common;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class ResolveConflictOperation(
        ResolveConflictCommandLineOptions options,
        IVmrForwardFlower forwardFlower,
        IVmrBackFlower backFlower,
        IVmrInfo vmrInfo,
        IVmrCloneManager vmrCloneManager,
        IRepositoryCloneManager repositoryCloneManager,
        IVmrDependencyTracker dependencyTracker,
        IDependencyFileManager dependencyFileManager,
        ILocalGitRepoFactory localGitRepoFactory,
        IBarApiClient barApiClient,
        IRemoteTokenProvider remoteTokenProvider,
        IFileSystem fileSystem,
        IProcessManager processManager,
        ILogger<ResolveConflictOperation> logger)
    : CodeFlowOperation(options, forwardFlower, backFlower, vmrInfo, vmrCloneManager, dependencyTracker, dependencyFileManager, localGitRepoFactory, barApiClient, fileSystem, logger)
{
    private readonly ResolveConflictCommandLineOptions _options = options;
    private readonly IVmrInfo _vmrInfo = vmrInfo;
    private readonly IVmrCloneManager _vmrCloneManager = vmrCloneManager;
    private readonly IRepositoryCloneManager _repositoryCloneManager = repositoryCloneManager;
    private readonly IProcessManager _processManager = processManager;
    private readonly IBarApiClient _barClient = barApiClient;
    private readonly IRemoteTokenProvider _remoteTokenProvider = remoteTokenProvider;
    private readonly ILogger<ResolveConflictOperation> _logger = logger;
    private readonly IFileSystem _fileSystem = fileSystem;

    private const string ResolveConflictCommitMessage =
        $$"""
        [{name}] Source update {oldShaShort}{{DarcLib.Constants.Arrow}}{newShaShort}
        Diff: {remote}/compare/{oldSha}..{newSha}
        
        From: {remote}/commit/{oldSha}
        To: {remote}/commit/{newSha}

        The following files had conflicts that were resolved by a user:

        {additionalMessage}

        """;

    protected override async Task<bool> ExecuteInternalAsync(
        string repoName,
        string? sourceDirectory,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        CancellationToken cancellationToken)
    {
        NativePath targetGitRepoPath = new(_processManager.FindGitRoot(Directory.GetCurrentDirectory()));

        var subscription = await FetchCodeflowSubscriptionAsync(_options.SubscriptionId);

        var pr = await FetchTrackedPrAsync(subscription.Id);

        var build = await FetchPrLastAppliedBuildAsync(pr);

        (var _, var repo) = await CloneReposAndFetchBranchesAsync(
            subscription,
            build,
            targetGitRepoPath,
            pr.HeadBranch,
            cancellationToken);

        if (!subscription.IsForwardFlow())
        {
            await ValidateLocalRepo(subscription, repo.Path, subscription.SourceDirectory);
        }

        return await ExecuteCodeflowAndPrepareCommitMessageAsync(
            subscription,
            build,
            repo,
            targetGitRepoPath,
            additionalRemotes,
            cancellationToken);
    }

    private async Task ValidateLocalRepo(Subscription subscription, NativePath repoPath, string mappingName)
    {
        var local = new Local(_remoteTokenProvider, _logger, repoPath);
        var sourceDependency = await local.GetSourceDependencyAsync();

        if (string.IsNullOrEmpty(sourceDependency?.Mapping))
        {
            throw new DarcException("The repository at the current working directory does not appear " +
                "to be part of a VMR (Virtual MonoRepo).");
        }

        if (!sourceDependency.Mapping.Equals(mappingName))
        {
            throw new DarcException("The current working directory does not match the subscription's " +
                $"source directory '{subscription.SourceDirectory}'.");
        }
    }

    private async Task<Subscription> FetchCodeflowSubscriptionAsync(string subscriptionId)
    {
        if (string.IsNullOrEmpty(subscriptionId))
        {
            throw new ArgumentException("Please specify a subscription id.");
        }

        _logger.LogInformation("Fetching subscription {subscriptionId}...", _options.SubscriptionId);

        var subscription = await _barClient.GetSubscriptionAsync(subscriptionId)
            ?? throw new DarcException($"No subscription found with id `{subscriptionId}`.");

        if (!subscription.SourceEnabled)
        {
            throw new DarcException($"Subscription with id `{subscription.Id}` is not a codeflow subscription.");
        }

        return subscription;
    }

    private async Task<Build> FetchPrLastAppliedBuildAsync(TrackedPullRequest pr)
    {
        _logger.LogInformation("Fetching build to apply...");

        var build = await _barClient.GetBuildAsync(pr.Updates.Last().BuildId);

        _logger.LogInformation("Build {buildId} / {buildName} found: {buildUrl}", build.Id, build.AzureDevOpsBuildNumber, build.GetBuildLink());

        return build;
    }

    private async Task<TrackedPullRequest> FetchTrackedPrAsync(Guid subscriptionId)
    {
        _logger.LogInformation("Fetching PR information...");

        TrackedPullRequest pr = await _barClient.GetTrackedPullRequestBySubscriptionIdAsync(subscriptionId)
            ?? throw new DarcException($"No PR was found for this subscription, or the PR is  already closed.");

        _logger.LogInformation("Found open PR: {prId}", pr.Url);

        return pr;
    }

    private async Task<string> GetLastFlownShaAsync(Subscription subscription, NativePath localPath)
    {
        var local = new Local(_remoteTokenProvider, _logger, localPath);

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

    private async Task<(ILocalGitRepo vmr, ILocalGitRepo repo)> CloneReposAndFetchBranchesAsync(
        Subscription subscription,
        Build build,
        NativePath targetRepoPath,
        string headBranch,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching PR branch {branchName}...", headBranch);

        ILocalGitRepo repo;
        ILocalGitRepo vmr;

        if (subscription.IsForwardFlow())
        {
            vmr = await _vmrCloneManager.PrepareVmrAsync(
                targetRepoPath,
                [subscription.TargetRepository],
                [headBranch],
                headBranch,
                resetToRemote: false,
                cancellationToken);

            _logger.LogInformation("Cloning source repository {repoUrl} at commit {commit}...",
                build.GetRepository(),
                DarcLib.Commit.GetShortSha(build.Commit));

            repo = await _repositoryCloneManager.PrepareCloneAsync(
                build.GetRepository(),
                build.Commit,
                cancellationToken: cancellationToken);

            _logger.LogInformation("Cloned {repoName} into {path}", subscription.TargetDirectory, repo.Path);
        }
        else
        {
            repo = await _repositoryCloneManager.PrepareCloneAsync(
                targetRepoPath,
                [subscription.TargetRepository],
                [headBranch],
                headBranch,
                resetToRemote: false,
                cancellationToken);

            _logger.LogInformation("Cloning VMR {repoUrl} at commit {commit}...",
                build.GetRepository(),
                DarcLib.Commit.GetShortSha(build.Commit));

            vmr = await _vmrCloneManager.PrepareVmrAsync(
                [build.GetRepository()],
                [build.Commit],
                build.Commit,
                resetToRemote: false,
                cancellationToken);

            _logger.LogInformation("Cloned the VMR into {path}", vmr.Path);
        }

        _vmrInfo.VmrPath = vmr.Path;
        return (vmr, repo);
    }

    private async Task<bool> ExecuteCodeflowAndPrepareCommitMessageAsync(
        Subscription subscription,
        Build build,
        ILocalGitRepo productRepo,
        NativePath targetGitRepoPath,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        CancellationToken cancellationToken)
    {
        CodeFlowResult result = await FlowCodeLocallyAsync(
            productRepo.Path,
            isForwardFlow: subscription.IsForwardFlow(),
            build: build,
            subscription: subscription,
            cancellationToken: cancellationToken);

        if (result.HadConflicts)
        {
            _logger.LogInformation("Codeflow has finished, and {conflictedFiles} conflicting file(s) have been" +
                " left on the current branch:", result.ConflictedFiles.Count);
            StringBuilder str = new();
            foreach (var conflict in result.ConflictedFiles)
            {
                str.AppendLine($" - {conflict}");
            }
            _logger.LogInformation("Please resolve the conflicts locally, commit and push your changes to unblock the codeflow PR.");

            string lastFlownSha = await GetLastFlownShaAsync(subscription, targetGitRepoPath);

            CreateCommitMessageFile(targetGitRepoPath, subscription, build, lastFlownSha, result.ConflictedFiles);
        }
        else
        {
            _logger.LogInformation(
                "Codeflow has finished and changes have been staged on the local branch with no conflicts encountered.");
        }

        return true;
    }

    private void CreateCommitMessageFile(
        string targetRepoPath,
        Subscription subscription,
        Build build,
        string lastFlownSha,
        IEnumerable<UnixPath> conflictedFiles)
    {
        // Overwrite .git/SQUASH_MSG to preset the commit message. This file is created by the codeflow's squash rebase.
        var commitEditMsgPath = Path.Combine(targetRepoPath, ".git", "SQUASH_MSG");

        var mappingName = subscription.IsForwardFlow()
            ? subscription.TargetDirectory
            : subscription.SourceDirectory;

        var conflictedFilesBlurb = string.Join(
            Environment.NewLine,
            conflictedFiles.Select(f => $"- {f}"));

        var commitMessage = VmrManagerBase.PrepareCommitMessage(
            ResolveConflictCommitMessage,
            mappingName,
            subscription.SourceRepository,
            lastFlownSha,
            build.Commit,
            additionalMessage: conflictedFilesBlurb);

        _fileSystem.WriteToFile(commitEditMsgPath, commitMessage);
    }
}
