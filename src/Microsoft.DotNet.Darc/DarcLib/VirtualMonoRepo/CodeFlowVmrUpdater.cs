// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Maestro.Common;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface ICodeFlowVmrUpdater
{
    /// <summary>
    /// Updates a repository inside of the VMR with the provided build. In special cases we set the source-manifest.json commitSha for the given repo
    /// to the zero commit to "trick" the algorithm. In these cases we also pass the fromSha parameter to correctly generate the commit message.
    /// This parameter is never used for the actual flow of the code
    /// </summary>
    /// <param name="mapping">Repository inside of the VMR we're updating</param>
    /// <param name="build">Build we're updating the VMR with</param>
    /// <param name="fromSha">The actual sha of the repository we're updating from.
    /// In some cases, we set the source-manifest json current sha to the git empty commit.
    /// When this happens, this parameter should be used to generate the correct commit message</param>
    /// <param name="additionalFileExclusions">List of files we don't want to update</param>
    /// <param name="resetToRemoteWhenCloningRepo">Weather or not to reset the branch to remote during cloning. Should be set to false when cloning a specific sha</param>
    /// <param name="keepConflicts">Preserve file changes with conflict markers when conflicts occur</param>
    Task<CodeFlowResult> UpdateRepository(
        SourceMapping mapping,
        Build build,
        string[]? additionalFileExclusions = null,
        string? fromSha = null,
        bool resetToRemoteWhenCloningRepo = false,
        bool keepConflicts = false,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// This class is able to update an individual repository within the VMR from one commit to another.
/// It creates git diffs while adhering to cloaking rules, resolving submodules..
/// This implementation is meant to be used within the full codeflow.
/// </summary>
public class CodeFlowVmrUpdater : VmrManagerBase, ICodeFlowVmrUpdater
{
    // Message used when synchronizing multiple commits as one
    public const string SyncCommitMessage =
        $$"""
        [{name}] Source update {oldShaShort}{{Constants.Arrow}}{newShaShort}
        Diff: {remote}/compare/{oldSha}..{newSha}
        
        From: {remote}/commit/{oldSha}
        To: {remote}/commit/{newSha}
        
        {{Constants.AUTOMATION_COMMIT_TAG}}
        """;
    private readonly IVmrInfo _vmrInfo;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly IRepositoryCloneManager _cloneManager;
    private readonly ILogger<VmrUpdater> _logger;
    private readonly ISourceManifest _sourceManifest;

    public CodeFlowVmrUpdater(
        IVmrInfo vmrInfo,
        IVmrDependencyTracker dependencyTracker,
        IRepositoryCloneManager cloneManager,
        IVmrPatchHandler patchHandler,
        IThirdPartyNoticesGenerator thirdPartyNoticesGenerator,
        ICodeownersGenerator codeownersGenerator,
        ICredScanSuppressionsGenerator credScanSuppressionsGenerator,
        ILocalGitClient localGitClient,
        ILocalGitRepoFactory localGitRepoFactory,
        IGitRepoFactory gitRepoFactory,
        IFileSystem fileSystem,
        ILogger<VmrUpdater> logger,
        ISourceManifest sourceManifest)
        : base(vmrInfo, dependencyTracker, patchHandler, thirdPartyNoticesGenerator, codeownersGenerator, credScanSuppressionsGenerator, localGitClient, localGitRepoFactory, logger)
    {
        _logger = logger;
        _sourceManifest = sourceManifest;
        _vmrInfo = vmrInfo;
        _dependencyTracker = dependencyTracker;
        _cloneManager = cloneManager;
    }

    public async Task<CodeFlowResult> UpdateRepository(
        SourceMapping mapping,
        Build build,
        string[]? additionalFileExclusions = null,
        string? fromSha = null,
        bool resetToRemoteWhenCloningRepo = false,
        bool keepConflicts = false,
        CancellationToken cancellationToken = default)
    {
        await _dependencyTracker.RefreshMetadataAsync();

        VmrDependencyVersion currentVersion = _dependencyTracker.GetDependencyVersion(mapping)
            ?? throw new Exception($"Failed to find current version for {mapping.Name}");

        // Do we need to change anything?
        if (currentVersion.Sha == build.Commit)
        {
            _logger.LogInformation("Repository {mappingName} is already at {targetRevision}",
                mapping.Name,
                build.Commit);
            return new CodeFlowResult(false, [], _vmrInfo.VmrPath, []);
        }

        var update = new VmrDependencyUpdate(
            mapping,
            build.GetRepository(),
            build.Commit,
            Parent: null,
            build.AzureDevOpsBuildNumber,
            build.Id);

        _logger.LogInformation("Synchronizing {name} from {current} to {repo} / {revision}",
            mapping.Name,
            fromSha ?? currentVersion.Sha,
            update.RemoteUri,
            build.Commit);

        // Add remotes for where we synced last from and where we are syncing to (e.g. github.com -> dev.azure.com)
        var remotes = new[]
            {
                mapping.DefaultRemote,
                update.RemoteUri,
                _sourceManifest.GetRepoVersion(mapping.Name).RemoteUri,
            }
            .Distinct()
            .OrderRemotesByLocalPublicOther()
            .ToArray();

        ILocalGitRepo clone = await _cloneManager.PrepareCloneAsync(
            mapping,
            remotes,
            requestedRefs: new[] { currentVersion.Sha, update.TargetRevision },
            checkoutRef: update.TargetRevision,
            resetToRemoteWhenCloningRepo,
            cancellationToken);

        fromSha ??= currentVersion.Sha;

        _logger.LogInformation("Updating VMR {repo} from {current} to {next}..",
            mapping.Name,
            Commit.GetShortSha(fromSha),
            Commit.GetShortSha(update.TargetRevision));

        var commitMessage = PrepareCommitMessage(
            SyncCommitMessage,
            mapping.Name,
            update.RemoteUri,
            fromSha,
            update.TargetRevision);

        try
        {
            var conflicts = await UpdateRepoToRevisionAsync(
                update,
                clone,
                currentVersion.Sha,
                commitMessage,
                restoreVmrPatches: false,
                keepConflicts,
                new CodeFlowParameters(
                    AdditionalRemotes: [.. remotes.Select(r => new AdditionalRemote(mapping.Name, r))],
                    TpnTemplatePath: _vmrInfo.ThirdPartyNoticesTemplateFullPath,
                    GenerateCodeOwners: false,
                    GenerateCredScanSuppressions: true),
                additionalFileExclusions,
                cancellationToken);

            return new CodeFlowResult(HadUpdates: true, conflicts, _vmrInfo.VmrPath, []);
        }
        catch (EmptySyncException e)
        {
            _logger.LogInformation(e.Message);
            return new CodeFlowResult(false, [], _vmrInfo.VmrPath, []);
        }
    }
}
