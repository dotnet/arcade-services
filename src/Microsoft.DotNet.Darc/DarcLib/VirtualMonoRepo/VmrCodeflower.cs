// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

/// <summary>
/// This class is responsible for taking changes done to a repo in the VMR and backflowing them into the repo.
/// It only makes patches/changes locally, no other effects are done.
/// </summary>
internal abstract class VmrCodeFlower
{
    private readonly IVmrInfo _vmrInfo;
    private readonly ISourceManifest _sourceManifest;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly ILocalGitClient _localGitClient;
    private readonly ILocalGitRepoFactory _localGitRepoFactory;
    private readonly IVersionDetailsParser _versionDetailsParser;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<VmrCodeFlower> _logger;

    protected readonly IBasicBarClient _barClient;

    protected VmrCodeFlower(
        IVmrInfo vmrInfo,
        ISourceManifest sourceManifest,
        IVmrDependencyTracker dependencyTracker,
        ILocalGitClient localGitClient,
        ILocalGitRepoFactory localGitRepoFactory,
        IVersionDetailsParser versionDetailsParser,
        IFileSystem fileSystem,
        IBasicBarClient barClient,
        ILogger<VmrCodeFlower> logger)
    {
        _vmrInfo = vmrInfo;
        _sourceManifest = sourceManifest;
        _dependencyTracker = dependencyTracker;
        _localGitClient = localGitClient;
        _localGitRepoFactory = localGitRepoFactory;
        _versionDetailsParser = versionDetailsParser;
        _fileSystem = fileSystem;
        _barClient = barClient;
        _logger = logger;
    }

    /// <summary>
    /// Main common entrypoint method that loads information about the last flow and calls the appropriate flow method.
    /// The algorithm is described in depth in the Unified Build documentation
    /// https://github.com/dotnet/arcade/blob/main/Documentation/UnifiedBuild/VMR-Full-Code-Flow.md#the-code-flow-algorithm
    /// </summary>
    /// <returns>True if there were changes to flow</returns>
    protected async Task<bool> FlowCodeAsync(
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
                cancellationToken);
        }

        if (!hasChanges)
        {
            // TODO: Clean up repos?
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
    /// <param name="headBranchExisted">Whether the PR branch already exists in the VMR. Null when we don't as the VMR needs to be prepared</param>
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
        CancellationToken cancellationToken);

    /// <summary>
    /// Finds a given line in a file and returns the SHA of the commit that last changed it.
    /// </summary>
    /// <param name="filePath">Path to the file</param>
    /// <param name="isTargetLine">Predicate to tell the line in question</param>
    /// <param name="blameFromCommit">Blame older commits than a given one</param>
    protected async Task<string> BlameLineAsync(string filePath, Func<string, bool> isTargetLine, string? blameFromCommit = null)
    {
        using (var stream = _fileSystem.GetFileStream(filePath, FileMode.Open, FileAccess.Read))
        using (var reader = new StreamReader(stream))
        {
            string? line;
            int lineNumber = 1;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (isTargetLine(line))
                {
                    return await _localGitClient.BlameLineAsync(_fileSystem.GetDirectoryName(filePath)!, filePath, lineNumber, blameFromCommit);
                }

                lineNumber++;
            }
        }

        throw new Exception($"Failed to blame file {filePath} - no matching line found");
    }

    /// <summary>
    /// Checks the last flows between a repo and a VMR and returns the most recent one.
    /// </summary>
    protected async Task<Codeflow> GetLastFlowAsync(SourceMapping mapping, ILocalGitRepo repoClone, bool currentIsBackflow)
    {
        await _dependencyTracker.RefreshMetadata();
        _sourceManifest.Refresh(_vmrInfo.SourceManifestPath);

        ForwardFlow lastForwardFlow = await GetLastForwardFlow(mapping.Name);
        Backflow? lastBackflow = await GetLastBackflow(repoClone.Path);

        if (lastBackflow is null)
        {
            return lastForwardFlow;
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
            throw new Exception($"Failed to find one or both commits {lastBackflow.VmrSha}, {lastForwardFlow.VmrSha} in {sourceRepo}");
        }

        // If the SHA's are the same, it's a commit created by inflow which was then flown out
        if (forwardSha == backwardSha)
        {
            return sourceRepo == repoClone ? lastForwardFlow : lastBackflow;
        }

        // Let's determine the last flow by comparing source commit of last backflow with target commit of last forward flow
        bool isForwardOlder = await IsAncestorCommit(sourceRepo, forwardSha, backwardSha);
        bool isBackwardOlder = await IsAncestorCommit(sourceRepo, backwardSha, forwardSha);

        // Commits not comparable
        if (isBackwardOlder == isForwardOlder)
        {
            // TODO: Figure out when this can happen and what to do about it
            throw new Exception($"Failed to determine which commit of {sourceRepo} is older ({backwardSha}, {forwardSha})");
        };

        return isBackwardOlder ? lastForwardFlow : lastBackflow;
    }

    /// <summary>
    /// Tries to resolve well-known conflicts that can occur during a code flow operation.
    /// The conflicts can happen when backward a forward flow PRs get merged out of order.
    /// This can be shown on the following schema (the order of events is numbered):
    /// 
    ///     repo                   VMR
    ///       O────────────────────►O
    ///       │  2.                 │ 1.
    ///       │   O◄────────────────O- - ┐
    ///       │   │            4.   │
    ///     3.O───┼────────────►O   │    │
    ///       │   │             │   │
    ///       │ ┌─┘             │   │    │
    ///       │ │               │   │
    ///     5.O◄┘               └──►O 6. │
    ///       │                 7.  │    O (actual branch for 7. is based on top of 1.)
    ///       |────────────────►O   │
    ///       │                 └──►O 8.
    ///       │                     │
    ///
    /// The conflict arises in step 8. and is caused by the fact that:
    ///   - When the forward flow PR branch is being opened in 7., the last sync (from the point of view of 5.) is from 1.
    ///   - This means that the PR branch will be based on 1. (the real PR branch is the "actual 7.")
    ///   - This means that when 6. merged, VMR's source-manifest.json got updated with the SHA of the 3.
    ///   - So the source-manifest in 6. contains the SHA of 3.
    ///   - The forward flow PR branch contains the SHA of 5.
    ///   - So the source-manifest file conflicts on the SHA (3. vs 5.)
    ///   - There's also a similar conflict in the git-info files.
    ///   - However, if only the version files are in conflict, we can try merging 6. into 7. and resolve the conflict.
    ///   - This is because basically we know we want to set the version files to point at 5.
    /// </summary>
    protected async Task<bool> TryMergingBranch(
        string mappingName,
        ILocalGitRepo repo,
        Build build,
        IReadOnlyCollection<string>? excludedAssets,
        string targetBranch,
        string branchToMerge,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Checking if target branch {targetBranch} has conflicts with {headBranch}", branchToMerge, targetBranch);

        await repo.CheckoutAsync(targetBranch);
        var result = await repo.RunGitCommandAsync(["merge", "--no-commit", "--no-ff", branchToMerge], cancellationToken);
        if (result.Succeeded)
        {
            try
            {
                await repo.CommitAsync(
                    $"Merging {branchToMerge} into {targetBranch}",
                    allowEmpty: false,
                    cancellationToken: CancellationToken.None);

                _logger.LogInformation("Successfully merged the branch {targetBranch} into {headBranch} in {repoPath}",
                    branchToMerge,
                    targetBranch,
                    repo.Path);
            }
            catch (Exception e) when (e.Message.Contains("nothing to commit"))
            {
                // Our branch might be fast-forward and so no commit is needed
                _logger.LogInformation("Branch {targetBranch} had no updates since it was last merged into {headBranch}",
                    branchToMerge,
                    targetBranch);
            }

            return true;
        }

        result = await repo.RunGitCommandAsync(["diff", "--name-only", "--diff-filter=U", "--relative"], cancellationToken);
        if (!result.Succeeded)
        {
            _logger.LogInformation("Failed to merge the branch {targetBranch} into {headBranch} in {repoPath}",
                branchToMerge,
                targetBranch,
                repo.Path);
            result = await repo.RunGitCommandAsync(["merge", "--abort"], CancellationToken.None);
            return false;
        }

        var conflictedFiles = result.StandardOutput
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => new UnixPath(line.Trim()));

        var unresolvableConflicts = conflictedFiles
            .Except(GetAllowedConflicts(conflictedFiles, mappingName))
            .ToList();

        if (unresolvableConflicts.Count > 0)
        {
            _logger.LogInformation("Failed to merge the branch {targetBranch} into {headBranch} due to unresolvable conflicts: {conflicts}",
                branchToMerge,
                targetBranch,
                string.Join(", ", unresolvableConflicts));

            result = await repo.RunGitCommandAsync(["merge", "--abort"], CancellationToken.None);
            return false;
        }

        if (!await TryResolveConflicts(
            mappingName,
            repo,
            build,
            excludedAssets,
            targetBranch,
            conflictedFiles,
            cancellationToken))
        {
            return false;
        }

        _logger.LogInformation("Successfully resolved file conflicts between branches {targetBranch} and {headBranch}",
            branchToMerge,
            targetBranch);

        try
        {
            await repo.CommitAsync(
                $"Merge branch {branchToMerge} into {targetBranch}",
                allowEmpty: false,
                cancellationToken: CancellationToken.None);
        }
        catch (Exception e) when (e.Message.Contains("Your branch is ahead of"))
        {
            // There was no reason to merge, we're fast-forward ahead from the target branch
        }

        return true;
    }

    protected virtual async Task<bool> TryResolveConflicts(
        string mappingName,
        ILocalGitRepo repo,
        Build build,
        IReadOnlyCollection<string>? excludedAssets,
        string targetBranch,
        IEnumerable<UnixPath> conflictedFiles,
        CancellationToken cancellationToken)
    {
        foreach (var filePath in conflictedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (await TryResolvingConflict(mappingName, repo, build, filePath, cancellationToken))
                {
                    continue;
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to resolve conflicts in {filePath}", filePath);
            }

            await repo.RunGitCommandAsync(["merge", "--abort"], CancellationToken.None);
            return false;
        }

        return true;
    }

    protected abstract Task<bool> TryResolvingConflict(
        string mappingName,
        ILocalGitRepo repo,
        Build build,
        string filePath,
        CancellationToken cancellationToken);

    protected abstract IEnumerable<UnixPath> GetAllowedConflicts(IEnumerable<UnixPath> conflictedFiles, string mappingName);

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
        string lastBackflowRepoSha = await BlameLineAsync(
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
        string lastForwardVmrSha = await BlameLineAsync(
            _vmrInfo.SourceManifestPath,
            line => line.Contains(lastForwardRepoSha));

        return new ForwardFlow(lastForwardRepoSha, lastForwardVmrSha);
    }

    /// <summary>
    /// Compares 2 git commits and returns true if the first one is an ancestor of the second one.
    /// </summary>
    private static async Task<bool> IsAncestorCommit(ILocalGitRepo repo, string parent, string ancestor)
    {
        var result = await repo.ExecuteGitCommand("merge-base", "--is-ancestor", parent, ancestor);

        // 0 - is ancestor
        // 1 - is not ancestor
        // other - invalid objects, other errors
        if (result.ExitCode > 1)
        {
            result.ThrowIfFailed($"Failed to determine which commit of {repo.Path} is older ({parent}, {ancestor})");
        }

        return result.ExitCode == 0;
    }

    protected abstract NativePath GetEngCommonPath(NativePath sourceRepo);
    protected abstract bool TargetRepoIsVmr();
}
