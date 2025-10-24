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
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client;
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
        IVmrDependencyTracker dependencyTracker,
        IDependencyFileManager dependencyFileManager,
        ILocalGitRepoFactory localGitRepoFactory,
        IBasicBarClient barApiClient,
        IFileSystem fileSystem,
        IProcessManager processManager,
        IProductConstructionServiceApi pcsApiClient,
        ILogger<ResolveConflictOperation> logger)
    : CodeFlowOperation(options, forwardFlower, backFlower, backflowConflictResolver, vmrInfo, vmrCloneManager, dependencyTracker, dependencyFileManager, localGitRepoFactory, barApiClient, fileSystem, logger)
{
    private readonly ResolveConflictCommandLineOptions _options = options;
    private readonly IVmrInfo _vmrInfo = vmrInfo;
    private readonly IProcessManager _processManager = processManager;
    private readonly ILocalGitRepoFactory _localGitRepoFactory = localGitRepoFactory;
    private readonly IBasicBarClient _barClient = barApiClient;
    private readonly IProductConstructionServiceApi _pcsApiClient = pcsApiClient;
    private readonly ILogger<ResolveConflictOperation> _logger = logger;

    protected override async Task ExecuteInternalAsync(
        string repoName,
        string? sourceDirectory,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        CancellationToken cancellationToken)
    {
        var subscription = await FetchCodeflowSubscriptionAsync(_options.SubscriptionId);

        var pr = await _pcsApiClient.PullRequest.GetTrackedPullRequestBySubscriptionIdAsync(
            subscription.Id.ToString(),
            cancellationToken);

        ValidateConflictingPrAsync(pr);

        var buildId = GetBuildIdFromTrackedPr(pr, subscription.Id);

        var targetGitRepoPath = new NativePath(_processManager.FindGitRoot(Directory.GetCurrentDirectory()));

        if (subscription.IsForwardFlow())
        {
            _vmrInfo.VmrPath = targetGitRepoPath;
            await ValidateLocalVmr(subscription);
        }
        else
        {
            await ValidateLocalRepo(subscription);
        }

        await ValidateLocalBranchMatchesRemote(
            targetGitRepoPath,
            subscription.TargetRepository,
            pr.HeadBranch);

        try
        {
            await FlowCodeLocallyAsync(
                targetGitRepoPath,
                isForwardFlow: subscription.IsForwardFlow(),
                additionalRemotes,
                cancellationToken,
                buildId: buildId);
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

    private static int GetBuildIdFromTrackedPr(TrackedPullRequest pr, Guid subscriptionId)
    {
        if (pr.NextBuildsToApply.TryGetValue(subscriptionId, out int buildId))
        {
            return buildId;
        }

        throw new InvalidOperationException("Encountered an unexpected exception: could not find the build to apply. "
            + "Please follow the instructions in the PR and seek assistance.");
    }

    #region Validations

    private async Task ValidateLocalVmr(Subscription subscription)
    {
        var mappingName = subscription.IsForwardFlow()
            ? subscription.TargetDirectory
            : subscription.SourceDirectory;

        var local = new Local(_options.GetRemoteTokenProvider(), _logger);

        SourceManifest sourceManifest;
        try
        {
            sourceManifest = await local.GetSourceManifestAsync(_vmrInfo.VmrPath);
        }
        catch (DependencyFileNotFoundException)
        {
            throw new DarcException("Could not find file `src/source-manifest.json` at the following" +
                $"git repository: `{_vmrInfo.VmrPath}`. Please make sure it is a correct path to the VMR.");
        }

        if (!sourceManifest.Repositories.Any(repo => repo.Path.Equals(mappingName)))
        {
            throw new DarcException($"Could not find repo with name '{mappingName}' in the source-manifest.json" +
                $" at the following git repository: `{_vmrInfo.VmrPath}. Please make sure it is a correct path to" +
                " the VMR and that the mapping exists.");
        }
    }

    private async Task ValidateLocalRepo(Subscription subscription)
    {
        var mappingName = subscription.IsForwardFlow()
            ? subscription.TargetDirectory
            : subscription.SourceDirectory;

        var local = new Local(_options.GetRemoteTokenProvider(), _logger);
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

    private static void ValidateConflictingPrAsync(TrackedPullRequest pr)
    {
        if (pr == null)
        {
            throw new DarcException($"No tracked pull request found for the provided subscription.");
        }

        if (pr.IsInConflict != true)
        {
            throw new DarcException("The pull request is currently not in conflict - there is nothing to resolve.");
        }
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
        var currentSha = await repo.GetShaForRefAsync(targetRepoPath);

        if (!currentSha.Equals(remoteSha))
        {
            throw new DarcException($"The current local branch '{currentSha}' does not match the PR head branch at remote " +
                $"('{prHeadBranch}'). Please checkout the correct branch and fetch the latest changes.");
        }
    }

#endregion
}
