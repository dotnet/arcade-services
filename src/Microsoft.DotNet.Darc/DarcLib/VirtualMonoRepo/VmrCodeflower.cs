// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IVmrCodeFlower
{
    Task<(Codeflow LastFlow, Backflow? LastBackFlow, ForwardFlow LastForwardFlow)> GetLastFlowsAsync(
        SourceMapping mapping,
        ILocalGitRepo repoClone,
        bool currentIsBackflow);

    Task<bool> FlowCodeAsync(
        Codeflow lastFlow,
        Codeflow currentFlow,
        ILocalGitRepo repo,
        SourceMapping mapping,
        Build build,
        IReadOnlyCollection<string>? excludedAssets,
        string targetBranch,
        string headBranch,
        bool discardPatches,
        bool headBranchExisted,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// This class is responsible for taking changes done to a repo in the VMR and backflowing them into the repo.
/// It only makes patches/changes locally, no other effects are done.
/// </summary>
public abstract class VmrCodeFlower : IVmrCodeFlower
{
    private readonly IVmrInfo _vmrInfo;
    private readonly ISourceManifest _sourceManifest;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly ILocalGitClient _localGitClient;
    private readonly ILocalGitRepoFactory _localGitRepoFactory;
    private readonly IVersionDetailsParser _versionDetailsParser;
    private readonly ILogger<VmrCodeFlower> _logger;

    protected IVmrInfo VmrInfoInstance => _vmrInfo;

    protected VmrCodeFlower(
        IVmrInfo vmrInfo,
        ISourceManifest sourceManifest,
        IVmrDependencyTracker dependencyTracker,
        ILocalGitClient localGitClient,
        ILocalGitRepoFactory localGitRepoFactory,
        IVersionDetailsParser versionDetailsParser,
        ILogger<VmrCodeFlower> logger)
    {
        _vmrInfo = vmrInfo;
        _sourceManifest = sourceManifest;
        _dependencyTracker = dependencyTracker;
        _localGitClient = localGitClient;
        _localGitRepoFactory = localGitRepoFactory;
        _versionDetailsParser = versionDetailsParser;
        _logger = logger;
    }

    /// <summary>
    /// Main common entrypoint method that loads information about the last flow and calls the appropriate flow method.
    /// The algorithm is described in depth in the Unified Build documentation
    /// https://github.com/dotnet/dotnet/tree/main/docs/VMR-Full-Code-Flow.md#the-code-flow-algorithm
    /// </summary>
    /// <returns>True if there were changes to flow</returns>
    public async Task<bool> FlowCodeAsync(
        Codeflow lastFlow,
        Codeflow currentFlow,
        ILocalGitRepo repo,
        SourceMapping mapping,
        Build build,
        IReadOnlyCollection<string>? excludedAssets,
        string targetBranch,
        string headBranch,
        bool discardPatches,
        bool headBranchExisted,
        CancellationToken cancellationToken = default)
    {
        if (lastFlow.SourceSha == currentFlow.SourceSha)
        {
            _logger.LogInformation("No new commits to flow from {sourceRepo}", currentFlow is Backflow ? "VMR" : mapping.Name);
            return false;
        }

        _logger.LogInformation("Last flow was {type} flow: {sourceSha} -> {targetSha}",
            lastFlow.Name,
            lastFlow.SourceSha,
            lastFlow.TargetSha);

        bool hasChanges;
        if (lastFlow.Name == currentFlow.Name)
        {
            _logger.LogInformation("Current flow is in the same direction");
            hasChanges = await SameDirectionFlowAsync(
                mapping,
                lastFlow,
                currentFlow,
                repo,
                build,
                excludedAssets,
                targetBranch,
                headBranch,
                discardPatches,
                headBranchExisted,
                cancellationToken);
        }
        else
        {
            _logger.LogInformation("Current flow is in the opposite direction");
            hasChanges = await OppositeDirectionFlowAsync(
                mapping,
                lastFlow,
                currentFlow,
                repo,
                build,
                targetBranch,
                headBranch,
                discardPatches,
                headBranchExisted,
                cancellationToken);
        }

        if (!hasChanges)
        {
            _logger.LogInformation("Nothing to flow from {sourceRepo}", currentFlow is Backflow ? "VMR" : mapping.Name);
        }

        return hasChanges;
    }

    /// <summary>
    /// Handles flowing changes that succeed a flow that was in the same direction (outgoing from the source repo).
    /// The changes that are flown are taken from a simple patch of changes that occurred since the last flow.
    /// </summary>
    /// <param name="mapping">Mapping to flow</param>
    /// <param name="lastFlow">Last flow that happened for the given mapping</param>
    /// <param name="currentFlow">Current flow that is being flown</param>
    /// <param name="repo">Local git repo clone of the source repo</param>
    /// <param name="build">Build with assets (dependencies) that is being flown</param>
    /// <param name="excludedAssets">Assets to exclude from the dependency flow</param>
    /// <param name="targetBranch">Target branch to create the PR against. If target branch does not exist, it is created off of this branch</param>
    /// <param name="headBranch">New/existing branch to make the changes on</param>
    /// <param name="discardPatches">If true, patches are deleted after applying them</param>
    /// <param name="headBranchExisted">Did we just create the headbranch or are we updating an existing one?</param>
    /// <returns>True if there were changes to flow</returns>
    protected abstract Task<bool> SameDirectionFlowAsync(
        SourceMapping mapping,
        Codeflow lastFlow,
        Codeflow currentFlow,
        ILocalGitRepo repo,
        Build build,
        IReadOnlyCollection<string>? excludedAssets,
        string targetBranch,
        string headBranch,
        bool discardPatches,
        bool headBranchExisted,
        CancellationToken cancellationToken);

    /// <summary>
    /// Handles flowing changes that succeed a flow that was in the opposite direction (incoming in the source repo).
    /// The changes that are flown are taken from a diff of repo contents and the last sync point from the last flow.
    /// </summary>
    /// <param name="mapping">Mapping to flow</param>
    /// <param name="lastFlow">Last flow that happened for the given mapping</param>
    /// <param name="currentFlow">Current flow that is being flown</param>
    /// <param name="repo">Local git repo clone of the source repo</param>
    /// <param name="build">Build with assets (dependencies) that is being flown</param>
    /// <param name="targetBranch">Target branch to create the PR against. If target branch does not exist, it is created off of this branch</param>
    /// <param name="headBranch">New/existing branch to make the changes on</param>
    /// <param name="discardPatches">If true, patches are deleted after applying them</param>
    /// <param name="headBranchExisted">Did we just create the headbranch or are we updating an existing one?</param>
    /// <returns>True if there were changes to flow</returns>
    protected abstract Task<bool> OppositeDirectionFlowAsync(
        SourceMapping mapping,
        Codeflow lastFlow,
        Codeflow currentFlow,
        ILocalGitRepo repo,
        Build build,
        string targetBranch,
        string headBranch,
        bool discardPatches,
        bool headBranchExisted,
        CancellationToken cancellationToken);

    /// <summary>
    /// Checks the last flows between a repo and a VMR and returns the most recent one.
    /// </summary>
    public async Task<(Codeflow LastFlow, Backflow? LastBackFlow, ForwardFlow LastForwardFlow)> GetLastFlowsAsync(
        SourceMapping mapping,
        ILocalGitRepo repoClone,
        bool currentIsBackflow)
    {
        await _dependencyTracker.RefreshMetadata();
        _sourceManifest.Refresh(_vmrInfo.SourceManifestPath);

        ForwardFlow lastForwardFlow = await GetLastForwardFlow(mapping.Name);
        Backflow? lastBackflow = await GetLastBackflow(repoClone.Path);

        if (lastBackflow is null)
        {
            return (lastForwardFlow, lastBackflow, lastForwardFlow);
        }

        string backwardSha, forwardSha;
        ILocalGitRepo sourceRepo;
        if (currentIsBackflow)
        {
            (backwardSha, forwardSha) = (lastBackflow.VmrSha, lastForwardFlow.VmrSha);
            sourceRepo = _localGitRepoFactory.Create(_vmrInfo.VmrPath);
        }
        else
        {
            (backwardSha, forwardSha) = (lastBackflow.RepoSha, lastForwardFlow.RepoSha);
            sourceRepo = repoClone;
        }

        GitObjectType objectType1 = await sourceRepo.GetObjectTypeAsync(backwardSha);
        GitObjectType objectType2 = await sourceRepo.GetObjectTypeAsync(forwardSha);

        if (objectType1 != GitObjectType.Commit || objectType2 != GitObjectType.Commit)
        {
            throw new InvalidSynchronizationException($"Failed to find one or both commits {backwardSha}, {forwardSha} in {sourceRepo}");
        }

        // If the SHA's are the same, it's a commit created by inflow which was then flown out
        if (forwardSha == backwardSha)
        {
            return
            (
                sourceRepo == repoClone ? lastForwardFlow : lastBackflow,
                lastBackflow,
                lastForwardFlow
            );
        }

        // Let's determine the last flow by comparing source commit of last backflow with target commit of last forward flow
        bool isForwardOlder = await sourceRepo.IsAncestorCommit(forwardSha, backwardSha);
        bool isBackwardOlder = await sourceRepo.IsAncestorCommit(backwardSha, forwardSha);

        // Commits not comparable. This can happen in situations such as trying to synchronize an old repo commit on top of
        // a new VMR commit which had other synchronization with the repo since.
        if (isBackwardOlder == isForwardOlder)
        {
            throw new InvalidSynchronizationException($"Failed to determine which commit of {sourceRepo} is older ({backwardSha}, {forwardSha})");
        }

        // When the last backflow to our repo came from a different branch, we ignore it and return the last forward flow.
        // Some repositories do not snap branches for preview (e.g. razor, roslyn, msbuild), and forward-flow their main
        // branch into both the main branch and preview branch of the VMR.
        // Forward-flows into preview branches should not accept backflows as the previous flow on which to compute 
        // the deltas, because those commits come from the main branch VMR. In those cases, we should always return
        // the previous forward-flow into the same preview branch of the VMR.
        if (!currentIsBackflow && isForwardOlder)
        {
            var vmr = _localGitRepoFactory.Create(_vmrInfo.VmrPath);
            var currentVmrSha = await vmr.GetShaForRefAsync();

            // We can tell the above by checking if the current target VMR commit is a child of the last backflow commit.
            // For normal flows it should be, but for the case described above it will be on a different branch.
            if (!await vmr.IsAncestorCommit(lastBackflow.VmrSha, currentVmrSha))
            {
                _logger.LogWarning("Last detected backflow ({sha1}) from VMR is from a different branch than target VMR sha ({sha2}). " +
                    "Ignoring backflow and considering the last forward flow to be the last flow.",
                    lastBackflow.VmrSha,
                    currentVmrSha);
                
                return (
                    lastForwardFlow,
                    lastBackflow,
                    lastForwardFlow
                );
            }
        }

        return
        (
            isBackwardOlder ? lastForwardFlow : lastBackflow,
            lastBackflow,
            lastForwardFlow
        );
    }

    /// <summary>
    /// Finds the last backflow between a repo and a VMR.
    /// </summary>
    private async Task<Backflow?> GetLastBackflow(NativePath repoPath)
    {
        // Last backflow SHA comes from Version.Details.xml in the repo
        SourceDependency? source = _versionDetailsParser.ParseVersionDetailsFile(repoPath / VersionFiles.VersionDetailsXml).Source;
        if (source is null)
        {
            return null;
        }

        string lastBackflowVmrSha = source.Sha;
        string lastBackflowRepoSha = await _localGitClient.BlameLineAsync(
            repoPath / VersionFiles.VersionDetailsXml,
            line => line.Contains(VersionDetailsParser.SourceElementName) && line.Contains(lastBackflowVmrSha));

        return new Backflow(lastBackflowVmrSha, lastBackflowRepoSha);
    }

    /// <summary>
    /// Finds the last forward flow between a repo and a VMR.
    /// </summary>
    private async Task<ForwardFlow> GetLastForwardFlow(string mappingName)
    {
        ISourceComponent repoInVmr = _sourceManifest.GetRepoVersion(mappingName);

        // Last forward flow SHAs come from source-manifest.json in the VMR
        string lastForwardRepoSha = repoInVmr.CommitSha;
        string lastForwardVmrSha = await _localGitClient.BlameLineAsync(
            _vmrInfo.SourceManifestPath,
            line => line.Contains(lastForwardRepoSha));

        return new ForwardFlow(lastForwardRepoSha, lastForwardVmrSha);
    }

    protected abstract NativePath GetEngCommonPath(NativePath sourceRepo);
    protected abstract bool TargetRepoIsVmr();
}
