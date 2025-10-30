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
    private readonly ILocalGitRepoFactory _localGitRepoFactory = localGitRepoFactory;
    private readonly IBarApiClient _barClient = barApiClient;
    private readonly ILogger<ResolveConflictOperation> _logger = logger;

    protected override async Task ExecuteInternalAsync(
        string repoName,
        string? sourceDirectory,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching subscription information ...");
        var subscription = await FetchCodeflowSubscriptionAsync(_options.SubscriptionId);

        _logger.LogInformation("Fetching PR information ...");
        TrackedPullRequest pr = await _barClient.GetTrackedPullRequestBySubscriptionIdAsync(subscription.Id)
            ?? throw new DarcException($"No open PR found for this subscription");

        _logger.LogInformation("Fetching build to apply ...");

        Build build = await _barClient.GetBuildAsync(pr.Updates.Last().BuildId);

        NativePath targetGitRepoPath = new(_processManager.FindGitRoot(Directory.GetCurrentDirectory()));

        NativePath repoPath;
        ILocalGitRepo vmr;
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

            // Clone VMR to a temp location
            vmr = await _vmrCloneManager.PrepareVmrAsync(
                [build.GetRepository()],
                [build.Commit],
                build.Commit,
                resetToRemote: false,
                cancellationToken);

        }

        _vmrInfo.VmrPath = vmr.Path;

        await ValidateLocalRepo(subscription, repoPath);

        await ValidateLocalBranchMatchesRemote(
            targetGitRepoPath,
            subscription.TargetRepository,
            pr.HeadBranch);

        try
        {
            await FlowCodeLocallyAsync(
                repoPath,
                isForwardFlow: subscription.IsForwardFlow(),
                additionalRemotes,
                subscription,
                cancellationToken,
                buildId: build.Id); // TODO - Create an overload where we can pass the build
        }
        catch (PatchApplicationLeftConflictsException)
        {
            _logger.LogInformation("Codeflow has finished, and conflicting files have been left on the current branch.");
            _logger.LogInformation("Please resolve the conflicts in your local environment and push your changes to "
                + "the PR branch in order to unblock the codeflow PR.");
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
            throw new DarcException("The current working directory does not appear to be a repository managed by Darc.");
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

    private async Task ValidateLocalBranchMatchesRemote(
        NativePath targetRepoPath,
        string repoUrl,
        string prHeadBranch)
    {
        var result = await _processManager.ExecuteGit(targetRepoPath, "ls-remote", repoUrl, prHeadBranch);
        result.ThrowIfFailed(
            $"""
            An unexpected error occurred while trying to fetch the latest PR branch SHA from {repoUrl}. 
            {result.StandardError}
            """);

        var remoteSha = result.StandardOutput.Split('\t')[0].Trim();

        var repo = _localGitRepoFactory.Create(targetRepoPath);
        var currentSha = await repo.GetShaForRefAsync();

        if (!currentSha.Equals(remoteSha))
        {
            throw new DarcException($"The current local branch '{currentSha}' does not match the PR head branch at remote " +
                $"('{prHeadBranch}'). Please checkout the correct branch and fetch the latest changes.");
        }
    }
}
