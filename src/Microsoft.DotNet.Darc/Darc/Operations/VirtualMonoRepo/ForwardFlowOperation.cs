// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Maestro.Common;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging;
using BarBuild = Microsoft.DotNet.ProductConstructionService.Client.Models.Build;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class ForwardFlowOperation(
        ForwardFlowCommandLineOptions options,
        IVmrForwardFlower forwardFlower,
        IVmrBackFlower backFlower,
        IVmrInfo vmrInfo,
        IVmrCloneManager vmrCloneManager,
        IRepositoryCloneManager cloneManager,
        IVmrDependencyTracker dependencyTracker,
        IDependencyFileManager dependencyFileManager,
        ILocalGitRepoFactory localGitRepoFactory,
        IBasicBarClient barApiClient,
        IFileSystem fileSystem,
        IProcessManager processManager,
        IVersionDetailsParser versionDetailsParser,
        ILogger<ForwardFlowOperation> logger)
    : CodeFlowOperation(options, forwardFlower, backFlower, vmrInfo, vmrCloneManager, dependencyTracker, dependencyFileManager, localGitRepoFactory, barApiClient, fileSystem, logger)
{
    private readonly ForwardFlowCommandLineOptions _options = options;
    private readonly IVmrInfo _vmrInfo = vmrInfo;
    private readonly IRepositoryCloneManager _cloneManager = cloneManager;
    private readonly ILocalGitRepoFactory _localGitRepoFactory = localGitRepoFactory;
    private readonly IProcessManager _processManager = processManager;
    private readonly IVersionDetailsParser _versionDetailsParser = versionDetailsParser;
    private readonly ILogger<ForwardFlowOperation> _logger = logger;

    protected override async Task<bool> ExecuteInternalAsync(
        string repoName,
        string? sourceDirectory,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_options.VmrPath))
        {
            throw new DarcException("Please specify a path to a local clone of the VMR to flow the changes into.");
        }

        _vmrInfo.VmrPath = new NativePath(_options.VmrPath);

        ILocalGitRepo sourceRepo;
        BarBuild build;

        if (!TryGetCurrentRepoPath(out NativePath? currentRepoPath))
        {
            if (string.IsNullOrEmpty(_options.SubscriptionId))
            {
                throw new DarcException("Please call this command from within a git repository or specify a subscription id to flow from.");
            }

            // Clone the subscription's source repository
            (sourceRepo, build) = await CloneSourceRepoFromSubscriptionAsync(cancellationToken);
        }
        else
        {
            // We're in a git repo, if a subscription id is provided make sure the sourceRepo path matches the subscription source repo
            // if it's not, just use the current repo path
            (sourceRepo, build) = await GetSourceRepoPreferringLocalAsync(currentRepoPath, cancellationToken);
        }

        await _cloneManager.RegisterCloneAsync(sourceRepo.Path);

        // Ensure the source repo has the commit we want to flow
        // This will fetch from the remote if necessary
        _logger.LogInformation("Making sure commit '{commit}' is present in repository '{repo}'...", build.Commit, sourceRepo.Path);
        await _cloneManager.PrepareCloneAsync(
            sourceRepo.Path,
            [build.GetRepository()],
            [build.Commit],
            build.Commit,
            resetToRemote: false,
            cancellationToken: cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        var result = await FlowCodeLocallyAsync(
            sourceRepo.Path,
            isForwardFlow: true,
            build: build,
            subscription: null,
            cancellationToken: cancellationToken);

        return !result.HadConflicts;
    }

    private async Task<(ILocalGitRepo sourceRepo, BarBuild build)> GetSourceRepoPreferringLocalAsync(NativePath currentRepoPath, CancellationToken cancellationToken)
    {
        var subscription = await GetSubscriptionAsync();
        // if no subscription was provided then we'll just use the current repo
        if (subscription == null)
        {
            var sourceRepo = _localGitRepoFactory.Create(currentRepoPath);
            return (sourceRepo, await ResolveBuildFromOptionsAsync(sourceRepo, null));
        }

        // if subscription is provided and we're here, check if the currentRepoPath matches the subscription source repo
        var currentRepoSourceMapping = GetRepoSourceMapping(currentRepoPath);
        var (repoName, _) = GitRepoUrlUtils.GetRepoNameAndOwner(subscription.SourceRepository);
        if (repoName == currentRepoSourceMapping || (currentRepoSourceMapping == "nuget-client" && repoName == "nuget.client"))
        {
            return (_localGitRepoFactory.Create(currentRepoPath), await PopulateOptionsAndGetBuildFromSubscriptionAsync(subscription));
        }

        // current repo path does not match subscription source repo, clone the subscription's source repository
        _logger.LogWarning("The current repository does not match the source repository of subscription '{subscriptionId}'. Cloning the source repository '{sourceRepo}' to a tmp folder",
            subscription.Id,
            subscription.SourceRepository);
        return await CloneSourceRepoFromSubscriptionAsync(cancellationToken);
    }

    private string? GetRepoSourceMapping(NativePath repoPath)
    {
        var versionDetails = _versionDetailsParser.ParseVersionDetailsFile(repoPath / VersionFiles.VersionDetailsXml);
        return versionDetails.Source?.Mapping;
    }

    private async Task<(ILocalGitRepo sourceRepo, BarBuild build)> CloneSourceRepoFromSubscriptionAsync(CancellationToken cancellationToken)
    {
        var subscription = await GetSubscriptionAsync()
            ?? throw new DarcException($"Subscription {_options.SubscriptionId} could not be found.");

        if (!subscription.SourceEnabled)
        {
            throw new DarcException("Only source-enabled subscriptions (VMR code flow) are supported for forward flow operations.");
        }

        var build = await PopulateOptionsAndGetBuildFromSubscriptionAsync(subscription);

        _logger.LogInformation(
            "Cloning repository '{sourceRepo}' at commit '{commit}'...",
            subscription.SourceRepository,
            DarcLib.Commit.GetShortSha(build.Commit));

        var clonedRepo = await _cloneManager.PrepareCloneAsync(
            subscription.SourceRepository,
            build.Commit,
            resetToRemote: false,
            cancellationToken: cancellationToken);

        _options.Ref = build.Commit;

        return (clonedRepo, build);
    }

    private bool TryGetCurrentRepoPath([NotNullWhen(true)] out NativePath? sourceRepoPath)
    {
        try
        {
            sourceRepoPath = new NativePath(_processManager.FindGitRoot(Environment.CurrentDirectory));
            return true;

        }
        catch (Exception)
        {
            sourceRepoPath = null;
            return false;
        }
    }
}
