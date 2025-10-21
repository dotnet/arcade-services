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

internal class ResolveOperation(
        ResolveCommandLineOptions options,
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
        ILogger<ResolveOperation> logger)
    : CodeFlowOperation(options, forwardFlower, backFlower, backflowConflictResolver, vmrInfo, vmrCloneManager, dependencyTracker, dependencyFileManager, localGitRepoFactory, barApiClient, fileSystem, logger)
{
    private readonly ResolveCommandLineOptions _options = options;
    private readonly IVmrInfo _vmrInfo = vmrInfo;
    private readonly IProcessManager _processManager = processManager;
    private readonly ILocalGitRepoFactory _localGitRepoFactory = localGitRepoFactory;
    private readonly IBasicBarClient _barClient = barApiClient;
    private readonly IProductConstructionServiceApi _pcsApiClient = pcsApiClient;
    private readonly ILogger<ResolveOperation> _logger = logger;

    protected override async Task ExecuteInternalAsync(
        string repoName,
        string? sourceDirectory,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_options.SourceRepo))
        {
            throw new ArgumentException("Please specify a local path on disk to the source git repo" +
                " or VMR to flow from.");
        }

        var subscription = await FetchCodeflowSubscriptionAsync(_options.SubscriptionId);

        var pr = await _pcsApiClient.PullRequest.GetTrackedPullRequestBySubscriptionIdAsync(
            subscription.Id.ToString(),
            cancellationToken);

        ValidateConflictingPrAsync(pr);

        var buildId = GetBuildIdFromTrackedPr(pr, subscription.Id);

        var sourceGitRepoPath = new NativePath(_processManager.FindGitRoot(_options.SourceRepo));
        var targetGitRepoPath = new NativePath(_processManager.FindGitRoot(Directory.GetCurrentDirectory()));

        await ValidateLocalVmr(subscription);
        await ValidateLocalRepo(subscription);

        await ValidateLocalBranchMatchesRemote(targetGitRepoPath, pr.HeadBranch);

        _vmrInfo.VmrPath = string.IsNullOrEmpty(subscription.TargetDirectory)
            ? targetGitRepoPath
            : sourceGitRepoPath;

        Console.WriteLine("Ready to execute codeflow locally. Proceed? y/n");

        var confirmation = Console.ReadLine();

        if (confirmation != "y")
        {
            Console.WriteLine("Aborting resolve operation...");
            return;
        }
        else
        {
            Console.WriteLine("Proceeding with codeflow...");
        }

        await FlowCodeLocallyAsync(
            targetGitRepoPath,
            isForwardFlow: false,
            additionalRemotes,
            cancellationToken,
            buildId: buildId);

        Console.WriteLine("Codeflow has finished. Please resolve the conflicts and commit the changes. " +
            "Then run git vmr resolve --continue.");

        return;
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
        var mappingName = string.IsNullOrEmpty(subscription.TargetDirectory)
            ? subscription.SourceDirectory
            : subscription.TargetDirectory;

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
        var mappingName = string.IsNullOrEmpty(subscription.TargetDirectory)
            ? subscription.SourceDirectory
            : subscription.TargetDirectory;

        var local = new Local(_options.GetRemoteTokenProvider(), _logger);
        var sourceDependency = await local.GetSourceDependencyAsync();

        if (string.IsNullOrEmpty(sourceDependency?.Mapping))
        {
            throw new DarcException("The current working directory does not appear to be a repository managed by Darc.");
        }

        if (sourceDependency?.Mapping == null || !sourceDependency.Mapping.Equals(subscription.SourceDirectory))
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
        var subscription = await _barClient.GetSubscriptionAsync(subscriptionId);

        if (subscription == null)
        {
            throw new DarcException($"No subscription found with id `{subscriptionId}`.");
        }

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

    private async Task ValidateLocalBranchMatchesRemote(NativePath targetRepoPath, string prHead)
    {
        var repo = _localGitRepoFactory.Create(targetRepoPath);
        var currentSha = await repo.GetShaForRefAsync(targetRepoPath);
        if (!prHead.Equals(currentSha, StringComparison.OrdinalIgnoreCase))
        {
            throw new DarcException($"The current local branch '{currentSha}' does not match the pull request" +
                $" head branch '{prHead}'. Please checkout the correct and fetch the latest changes from the PR branch.");
        }
    }

#endregion
}
