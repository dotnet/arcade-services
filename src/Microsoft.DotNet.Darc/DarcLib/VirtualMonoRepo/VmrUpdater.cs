// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Maestro.Common;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

/// <summary>
/// This class is able to update an individual repository within the VMR from one commit to another.
/// It creates git diffs while adhering to cloaking rules, accommodating for patched files, resolving submodules.
/// </summary>
public class VmrUpdater : VmrManagerBase, IVmrUpdater
{
    // Message used when synchronizing multiple commits as one
    private const string SquashCommitMessage =
        $$"""
        [{name}] Sync {oldShaShort}{{Constants.Arrow}}{newShaShort}
        Diff: {remote}/compare/{oldSha}..{newSha}
        
        From: {remote}/commit/{oldSha}
        To: {remote}/commit/{newSha}
        
        {{Constants.AUTOMATION_COMMIT_TAG}}
        """;

    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly IRepositoryCloneManager _cloneManager;
    private readonly ILogger<VmrUpdater> _logger;
    private readonly ISourceManifest _sourceManifest;

    public VmrUpdater(
        IVmrDependencyTracker dependencyTracker,
        IRepositoryCloneManager cloneManager,
        IVmrPatchHandler patchHandler,
        IThirdPartyNoticesGenerator thirdPartyNoticesGenerator,
        ICodeownersGenerator codeownersGenerator,
        ICredScanSuppressionsGenerator credScanSuppressionsGenerator,
        ILocalGitClient localGitClient,
        ILocalGitRepoFactory localGitRepoFactory,
        IGitRepoFactory gitRepoFactory,
        ILogger<VmrUpdater> logger,
        ISourceManifest sourceManifest,
        IVmrInfo vmrInfo)
        : base(vmrInfo, dependencyTracker, patchHandler, thirdPartyNoticesGenerator, codeownersGenerator, credScanSuppressionsGenerator, localGitClient, localGitRepoFactory, logger)
    {
        _logger = logger;
        _sourceManifest = sourceManifest;
        _dependencyTracker = dependencyTracker;
        _cloneManager = cloneManager;
    }

    public async Task<bool> UpdateRepository(
        string mappingName,
        string? targetRevision,
        CodeFlowParameters codeFlowParameters,
        bool resetToRemoteWhenCloningRepo = false,
        bool keepConflicts = false,
        CancellationToken cancellationToken = default)
    {
        await _dependencyTracker.RefreshMetadataAsync();

        var mapping = _dependencyTracker.GetMapping(mappingName);

        var dependencyUpdate = new VmrDependencyUpdate(
            mapping,
            mapping.DefaultRemote,
            targetRevision ?? mapping.DefaultRef,
            Parent: null,
            OfficialBuildId: null,
            BarId: null);

        try
        {
            await UpdateRepositoryInternal(
                dependencyUpdate,
                restoreVmrPatches: true,
                codeFlowParameters,
                resetToRemoteWhenCloningRepo,
                keepConflicts,
                cancellationToken);
            return true;
        }
        catch (EmptySyncException e)
        {
            _logger.LogInformation(e.Message);
            return false;
        }
    }

    private async Task UpdateRepositoryInternal(
        VmrDependencyUpdate update,
        bool restoreVmrPatches,
        CodeFlowParameters codeFlowParameters,
        bool resetToRemoteWhenCloningRepo = false,
        bool keepConflicts = false,
        CancellationToken cancellationToken = default)
    {
        VmrDependencyVersion currentVersion = _dependencyTracker.GetDependencyVersion(update.Mapping)
            ?? throw new Exception($"Failed to find current version for {update.Mapping.Name}");

        // Do we need to change anything?
        if (currentVersion.Sha == update.TargetRevision)
        {
            throw new EmptySyncException($"Repository {update.Mapping.Name} is already at {update.TargetRevision}");
        }

        _logger.LogInformation("Synchronizing {name} from {current} to {repo} / {revision}",
            update.Mapping.Name,
            currentVersion.Sha,
            update.RemoteUri,
            update.TargetRevision);

        // Sort remotes so that we go Local -> GitHub -> AzDO
        // This makes the synchronization work even for cases when we can't access internal repos
        // For example, when we merge internal branches to public and the commits are already in public,
        // even though Version.Details.xml or source-manifest.json point to internal AzDO ones, we can still synchronize.
        var remotes = codeFlowParameters.AdditionalRemotes
            .Where(r => r.Mapping == update.Mapping.Name)
            .Select(r => r.RemoteUri)
            // Add remotes for where we synced last from and where we are syncing to (e.g. github.com -> dev.azure.com)
            .Append(_sourceManifest.GetRepoVersion(update.Mapping.Name).RemoteUri)
            .Append(update.RemoteUri)
            // Add the default remote
            .Prepend(update.Mapping.DefaultRemote)
            .Distinct()
            // Prefer local git repos, then GitHub, then AzDO
            .OrderRemotesByLocalPublicOther()
            .ToArray();

        ILocalGitRepo clone = await _cloneManager.PrepareCloneAsync(
            update.Mapping,
            remotes,
            requestedRefs: new[] { currentVersion.Sha, update.TargetRevision },
            checkoutRef: update.TargetRevision,
            resetToRemoteWhenCloningRepo,
            cancellationToken);

        update = update with
        {
            TargetRevision = await clone.GetShaForRefAsync(update.TargetRevision)
        };

        _logger.LogInformation("Updating {repo} from {current} to {next}..",
            update.Mapping.Name, Commit.GetShortSha(currentVersion.Sha), Commit.GetShortSha(update.TargetRevision));

        var commitMessage = PrepareCommitMessage(
            SquashCommitMessage,
            update.Mapping.Name,
            update.RemoteUri,
            currentVersion.Sha,
            update.TargetRevision);

        await UpdateRepoToRevisionAsync(
            update,
            clone,
            currentVersion.Sha,
            commitMessage,
            restoreVmrPatches,
            keepConflicts,
            codeFlowParameters,
            cancellationToken: cancellationToken);
    }

    private class RepositoryNotInitializedException(string message) : Exception(message)
    {
    }
}
