// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface ICodeFlowVmrUpdater
{
    Task<bool> UpdateRepository(
        SourceMapping mapping,
        Build build,
        string? fromSha = null,
        bool resetToRemoteWhenCloningRepo = false,
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
    private const string SyncCommitMessage =
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
        IVersionDetailsParser versionDetailsParser,
        IRepositoryCloneManager cloneManager,
        IVmrPatchHandler patchHandler,
        IThirdPartyNoticesGenerator thirdPartyNoticesGenerator,
        ICodeownersGenerator codeownersGenerator,
        ICredScanSuppressionsGenerator credScanSuppressionsGenerator,
        ILocalGitClient localGitClient,
        ILocalGitRepoFactory localGitRepoFactory,
        IDependencyFileManager dependencyFileManager,
        IGitRepoFactory gitRepoFactory,
        IWorkBranchFactory workBranchFactory,
        IFileSystem fileSystem,
        IBasicBarClient barClient,
        ILogger<VmrUpdater> logger,
        ISourceManifest sourceManifest)
        : base(vmrInfo, sourceManifest, dependencyTracker, patchHandler, versionDetailsParser, thirdPartyNoticesGenerator, codeownersGenerator, credScanSuppressionsGenerator, localGitClient, localGitRepoFactory, dependencyFileManager, barClient, fileSystem, logger)
    {
        _logger = logger;
        _sourceManifest = sourceManifest;
        _vmrInfo = vmrInfo;
        _dependencyTracker = dependencyTracker;
        _cloneManager = cloneManager;
    }

    public async Task<bool> UpdateRepository(
        SourceMapping mapping,
        Build build,
        string? fromSha = null,
        bool resetToRemoteWhenCloningRepo = false,
        CancellationToken cancellationToken = default)
    {
        await _dependencyTracker.RefreshMetadata();

        VmrDependencyVersion currentVersion = _dependencyTracker.GetDependencyVersion(mapping)
            ?? throw new Exception($"Failed to find current version for {mapping.Name}");

        // Do we need to change anything?
        if (currentVersion.Sha == build.Commit)
        {
            _logger.LogInformation("Repository {mappingName} is already at {targetRevision}",
                mapping.Name,
                build.Commit);
            return false;
        }

        var update = new VmrDependencyUpdate(
            mapping,
            build.GetRepository(),
            build.Commit,
            build.Assets.FirstOrDefault()?.Version,
            Parent: null,
            build.AzureDevOpsBuildNumber,
            build.Id);

        _logger.LogInformation("Synchronizing {name} from {current} to {repo} / {revision}",
            mapping.Name,
            currentVersion.Sha,
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

        if (currentVersion.Sha != Constants.EmptyGitObject)
        {
            fromSha = currentVersion.Sha;
        }
        else
        {
            if (string.IsNullOrEmpty(fromSha))
            {
                throw new ArgumentException($"{nameof(fromSha)} mustn't be null when {nameof(currentVersion.Sha)} is {Constants.EmptyGitObject}");
            }
        }

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
            await UpdateRepoToRevisionAsync(
                update,
                clone,
                currentVersion.Sha,
                commitMessage,
                restoreVmrPatches: false,
                new CodeFlowParameters(
                    AdditionalRemotes: [.. remotes.Select(r => new AdditionalRemote(mapping.Name, r))],
                    TpnTemplatePath: _vmrInfo.ThirdPartyNoticesTemplateFullPath,
                    GenerateCodeOwners: false,
                    GenerateCredScanSuppressions: true,
                    DiscardPatches: true,
                    ApplyAdditionalMappings: false),
                cancellationToken);

            return true;
        }
        catch (EmptySyncException e)
        {
            _logger.LogInformation(e.Message);
            return false;
        }
    }

    // VMR patches are not handled during full code flow
    protected override Task<IReadOnlyCollection<VmrIngestionPatch>> StripVmrPatchesAsync(
            IReadOnlyCollection<VmrIngestionPatch> patches,
            IReadOnlyCollection<AdditionalRemote> additionalRemotes,
            CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyCollection<VmrIngestionPatch>>([]);
}
