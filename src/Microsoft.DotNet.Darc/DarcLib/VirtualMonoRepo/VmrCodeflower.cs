// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
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

public interface IVmrCodeFlower
{
    Task<LastFlows> GetLastFlowsAsync(
        string mappingName,
        ILocalGitRepo repoClone,
        bool currentIsBackflow,
        bool ignoreNonLinearFlow);
}

public record CodeflowOptions(
    SourceMapping Mapping,
    Codeflow CurrentFlow,
    string TargetBranch,
    string HeadBranch,
    Build Build,
    IReadOnlyCollection<string>? ExcludedAssets,
    bool KeepConflicts,
    bool ForceUpdate,
    bool UnsafeFlow);

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
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<VmrCodeFlower> _logger;

    public const string FileToBeRemovedContent =
        $"""
        PLEASE READ

        Please remove this file during conflict resolution in your PR.
        This file has been reverted (removed) in the source repository but the PR branch
        does not have the file yet as it's based on an older commit. This means the file is
        not getting removed in the PR due to the other conflicts.
        """;

    protected VmrCodeFlower(
        IVmrInfo vmrInfo,
        ISourceManifest sourceManifest,
        IVmrDependencyTracker dependencyTracker,
        ILocalGitClient localGitClient,
        ILocalGitRepoFactory localGitRepoFactory,
        IVersionDetailsParser versionDetailsParser,
        IFileSystem fileSystem,
        ILogger<VmrCodeFlower> logger)
    {
        _vmrInfo = vmrInfo;
        _sourceManifest = sourceManifest;
        _dependencyTracker = dependencyTracker;
        _localGitClient = localGitClient;
        _localGitRepoFactory = localGitRepoFactory;
        _versionDetailsParser = versionDetailsParser;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    /// <summary>
    /// Main common entrypoint method that loads information about the last flow and calls the appropriate flow method.
    /// The algorithm is described in depth in the Unified Build documentation
    /// https://github.com/dotnet/dotnet/tree/main/docs/VMR-Full-Code-Flow.md#the-code-flow-algorithm
    /// </summary>
    /// <returns>True if there were changes to flow</returns>
    public async Task<CodeFlowResult> FlowCodeAsync(
        CodeflowOptions codeflowOptions,
        LastFlows lastFlows,
        ILocalGitRepo repo,
        bool headBranchExisted,
        CancellationToken cancellationToken = default)
    {
        var lastFlow = lastFlows.LastFlow;
        if (lastFlow.SourceSha == codeflowOptions.CurrentFlow.SourceSha)
        {
            _logger.LogInformation("No new commits to flow from {sourceRepo}", codeflowOptions.CurrentFlow is Backflow ? "VMR" : codeflowOptions.Mapping.Name);
            return new CodeFlowResult(false, [], repo.Path, []);
        }

        if (lastFlow.IsBackflow != codeflowOptions.CurrentFlow.IsBackflow && headBranchExisted && !codeflowOptions.ForceUpdate)
        {
            throw new BlockingCodeflowException("Cannot apply codeflow on PR head branch because an opposite direction flow has been merged.");
        }

        if (!codeflowOptions.UnsafeFlow)
        {
            await EnsureCodeflowLinearityAsync(repo, codeflowOptions.CurrentFlow, lastFlows);
        }

        _logger.LogInformation("Last flow was {type} flow: {sourceSha} -> {targetSha}",
            lastFlow.Name,
            lastFlow.SourceSha,
            lastFlow.TargetSha);

        CodeFlowResult result;
        if (lastFlow.IsBackflow == codeflowOptions.CurrentFlow.IsBackflow)
        {
            _logger.LogInformation("Current flow is in the same direction");
            result = await SameDirectionFlowAsync(
                codeflowOptions,
                lastFlows,
                repo,
                headBranchExisted,
                cancellationToken);
        }
        else
        {
            _logger.LogInformation("Current flow is in the opposite direction");
            result = await OppositeDirectionFlowAsync(
                codeflowOptions,
                lastFlows,
                repo,
                headBranchExisted,
                cancellationToken);
        }

        if (!result.HadUpdates)
        {
            _logger.LogInformation("Nothing to flow from {sourceRepo}", codeflowOptions.CurrentFlow is Backflow ? "VMR" : codeflowOptions.Mapping.Name);
        }

        return result;
    }

    /// <summary>
    /// Handles flowing changes that succeed a flow that was in the same direction (outgoing from the source repo).
    /// The changes that are flown are taken from a simple patch of changes that occurred since the last flow.
    /// </summary>
    /// <param name="lastFlows">Last flows that happened for the given mapping</param>
    /// <param name="repo">Local git repo clone of the source repo</param>
    /// <param name="headBranchExisted">Did we just create the headbranch or are we updating an existing one?</param>
    /// <returns>True if there were changes to flow</returns>
    protected abstract Task<CodeFlowResult> SameDirectionFlowAsync(
        CodeflowOptions codeflowOptions,
        LastFlows lastFlows,
        ILocalGitRepo repo,
        bool headBranchExisted,
        CancellationToken cancellationToken);

    /// <summary>
    /// Handles flowing changes that succeed a flow that was in the opposite direction (incoming in the source repo).
    /// The changes that are flown are taken from a diff of repo contents and the last sync point from the last flow.
    /// </summary>
    /// <param name="lastFlows">Last flows that happened for the given mapping</param>
    /// <param name="headBranchExisted">Did we just create the headbranch or are we updating an existing one?</param>
    /// <returns>True if there were changes to flow</returns>
    protected abstract Task<CodeFlowResult> OppositeDirectionFlowAsync(
        CodeflowOptions codeflowOptions,
        LastFlows lastFlows,
        ILocalGitRepo sourceRepo,
        bool headBranchExisted,
        CancellationToken cancellationToken);

    /// <summary>
    /// Tries to detect if given last flows (forward and backward) are crossing each other.
    /// In the following diagram, where we are creating the flow 7. the flows 3->6 and 1->5
    /// are crossing each other.
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
    ///       │                 └──►x 8.
    ///       │                     │
    ///
    /// This can cause problems when we're forming 7. and we detect the last flow to be 1.->5.
    /// 
    /// The conflict arises in step 8. and is caused by the fact that:
    ///   - When the forward flow PR branch is being opened in 7., the last sync (from the point of view of 5.) is from 1.
    ///   - This means that the PR branch will be based on 1. (the real PR branch is the "actual 7.")
    ///   - This means that when 6. merged, VMR's source-manifest.json got updated with the SHA of the 3.
    ///   - So the source-manifest in 6. contains the SHA of 3.
    ///   - The forward flow PR branch contains the SHA of 5.
    ///   - So the source-manifest file conflicts on the SHA (3. vs 5.)
    ///   - However, if only the version files are in conflict, we can try merging 6. into 7. and resolve the conflict.
    ///   - This is because basically we know we want to set the version files to point at 5.
    /// of commit 1. and 6.
    /// </summary>
    /// <returns>Null, if the last flow is the most recent flow for both sides. otherwise the other crossing flow.</returns>
    protected abstract Task<Codeflow?> DetectCrossingFlow(
        Codeflow lastFlow,
        Backflow? lastBackFlow,
        ForwardFlow lastForwardFlow,
        ILocalGitRepo repo);

    /// <summary>
    /// Checks the last flows between a repo and a VMR and returns the most recent one.
    /// </summary>
    public async Task<LastFlows> GetLastFlowsAsync(
        string mappingName,
        ILocalGitRepo repoClone,
        bool currentIsBackflow,
        bool ignoreNonLinearFlow)
    {
        await _dependencyTracker.RefreshMetadataAsync();
        _sourceManifest.Refresh(_vmrInfo.SourceManifestPath);

        ForwardFlow lastForwardFlow = await GetLastForwardFlow(mappingName);
        Backflow? lastBackflow = await GetLastBackflow(repoClone.Path);

        if (lastBackflow is null)
        {
            return new LastFlows(lastForwardFlow, lastBackflow, lastForwardFlow, CrossingFlow: null);
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
            Codeflow lastflow = sourceRepo == repoClone ? lastForwardFlow : lastBackflow;
            return new LastFlows(
                lastflow,
                lastBackflow,
                lastForwardFlow,
                await DetectCrossingFlow(lastflow, lastBackflow, lastForwardFlow, repoClone));
        }

        // Let's determine the last flow by comparing source commit of last backflow with target commit of last forward flow
        bool isForwardOlder = await sourceRepo.IsAncestorCommit(forwardSha, backwardSha);
        bool isBackwardOlder = await sourceRepo.IsAncestorCommit(backwardSha, forwardSha);

        // Commits not comparable. This can happen in situations such as trying to synchronize an old repo commit on top of
        // a new VMR commit which had other synchronization with the repo since.
        // It can also happen if we get a flow to a branch that previously received flows from a different branch
        //
        //     repo                   VMR
        //       │                     │ 1.
        //       │                     O────┐
        //       │                  2. │    │
        //     3.O◄────────────────────O    │
        //       │                     │    │
        //       │  ??? ◄──────────────┼────O 4.
        //       │                     │
        //
        // In such a case, we cannot compare commits 2. and 4.
        if (isBackwardOlder == isForwardOlder)
        {
            if (ignoreNonLinearFlow)
            {
                _logger.LogWarning("Encountered problems with commit history linearity but will bypass because of the unsafe mode override");

                return new LastFlows(
                    // When ignoring non-linear flows, we should do an opposite direction flow
                    LastFlow: sourceRepo != repoClone ? lastBackflow : lastForwardFlow,
                    LastBackFlow: lastBackflow,
                    LastForwardFlow: lastForwardFlow,
                    CrossingFlow: null);
            }

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

                return new LastFlows(
                    LastFlow: lastForwardFlow,
                    LastBackFlow: lastBackflow,
                    LastForwardFlow: lastForwardFlow,
                    await DetectCrossingFlow(lastForwardFlow, lastBackflow, lastForwardFlow, repoClone));
            }
        }

        Codeflow lastFlow = isBackwardOlder ? lastForwardFlow : lastBackflow;
        return new LastFlows(
            LastFlow: lastFlow,
            LastBackFlow: lastBackflow,
            LastForwardFlow: lastForwardFlow,
            await DetectCrossingFlow(lastFlow, lastBackflow, lastForwardFlow, repoClone));
    }

    /// <summary>
    /// Attempts to apply code flow patches to the target branch. When conflicting, rebases to an older commit,
    /// recreates previous flows and applies the changes on top of that.
    /// </summary>
    protected async Task<CodeFlowResult> ApplyChangesWithRecreationFallbackAsync(
        CodeflowOptions codeflowOptions,
        LastFlows lastFlows,
        ILocalGitRepo productRepo,
        bool headBranchExisted,
        IWorkBranch? workBranch,
        ApplyLatestChangesDelegate applyLatestChanges,
        CancellationToken cancellationToken)
    {
        try
        {
            return await applyLatestChanges(enableRebase: false);
        }
        catch (PatchApplicationFailedException)
        {
            // We need to recreate a previous flow so that we have something to rebase later
            return await RecreatePreviousFlowsAndApplyChanges(
                codeflowOptions with
                {
                    HeadBranch = workBranch!.WorkBranchName,
                },
                productRepo,
                lastFlows,
                async (_) => await applyLatestChanges(enableRebase: false),
                cancellationToken);
        }
    }

    /// <summary>
    /// Tries to find how many previous flows need to be recreated in order to apply the current changes.
    /// Iteratively rewinds through previous flows until it finds the one that introduced the conflict with the current changes.
    /// Then it creates a given branch there and applies the current changes on top of the recreated previous flows.
    /// </summary>
    private async Task<CodeFlowResult> RecreatePreviousFlowsAndApplyChanges(
        CodeflowOptions codeflowOptions,
        ILocalGitRepo repo,
        LastFlows lastFlows,
        ApplyLatestChangesDelegate reapplyChanges,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Failed to flow changes because of a conflict. Rebasing onto an older commit and recreating previous flows..");

        // Create a fake previously applied build that we will use when reapplying the previous flow.
        // We only care about the sha here, because it will get overwritten anyway with the current build which will be applied on top.
        bool currentIsBackflow = codeflowOptions.CurrentFlow is Backflow;
        var lastFlownSha = currentIsBackflow ? lastFlows.LastBackFlow!.VmrSha : lastFlows.LastForwardFlow.RepoSha;
        Build previouslyAppliedBuild = new(-1, DateTimeOffset.Now, 0, false, false, lastFlownSha, [], [], [], [])
        {
            GitHubRepository = codeflowOptions.Build.GitHubRepository,
            AzureDevOpsRepository = codeflowOptions.Build.AzureDevOpsRepository
        };

        Codeflow? previousFlow = null;
        LastFlows? previousFlows = null;

        // We recursively try to re-create previous flows until we find the one that introduced the conflict with the current flown
        int flowsToRecreate = 1;
        while (flowsToRecreate < 50)
        {
            _logger.LogInformation("Trying to recreate {count} previous flow(s)..", flowsToRecreate);

            // We rewing to the previous flow and create a branch there
            (previousFlow, previousFlows) = await UnwindPreviousFlowAsync(
                codeflowOptions.Mapping,
                repo,
                previousFlows ?? lastFlows,
                codeflowOptions.HeadBranch,
                codeflowOptions.TargetBranch,
                codeflowOptions.UnsafeFlow,
                cancellationToken);

            // We store the SHA where the head branch originates from so that later we can diff it
            var shaBeforeRecreation = currentIsBackflow
                ? await repo.GetShaForRefAsync()
                : await _localGitClient.GetShaForRefAsync(_vmrInfo.VmrPath);

            // We replay the previous flows, excluding manual changes that might have caused the conflict
            try
            {
                await FlowCodeAsync(
                    codeflowOptions with
                    {
                        Build = previouslyAppliedBuild,
                        CurrentFlow = currentIsBackflow
                            ? new Backflow(previouslyAppliedBuild.Commit, previousFlow!.RepoSha)
                            : new ForwardFlow(previouslyAppliedBuild.Commit, previousFlow!.VmrSha),
                        KeepConflicts = false,
                        ForceUpdate = true,
                    },
                    previousFlows,
                    repo,
                    headBranchExisted: true, // Head branch was created when we rewound to the previous flow
                    cancellationToken);
            }
            catch (Exception e) when (e is PatchApplicationFailedException)
            {
                _logger.LogInformation("Recreated {count} flows but current changes conflict with them. Recreating deeper...", flowsToRecreate);
                flowsToRecreate++;
                continue;
            }

            var changedFilesAfterRecreation = await GetChangesInHeadBranch(
                codeflowOptions.Mapping,
                repo,
                currentIsBackflow,
                shaBeforeRecreation,
                cancellationToken);

            // We apply the current changes on top again to check if they apply now
            try
            {
                CodeFlowResult result = await reapplyChanges(enableRebase: false);

                await HandleRevertedFiles(
                    codeflowOptions,
                    repo,
                    shaBeforeRecreation,
                    changedFilesAfterRecreation,
                    cancellationToken);

                _logger.LogInformation("Successfully recreated {count} flows and applied new changes from {sha}",
                    flowsToRecreate,
                    codeflowOptions.Build.Commit);

                return result with
                {
                    RecreatedPreviousFlows = true,
                };
            }
            catch (Exception e) when (e is PatchApplicationFailedException)
            {
                _logger.LogInformation("Recreated {count} flows but conflict with a previous flow still exists. Recreating deeper...", flowsToRecreate);
                flowsToRecreate++;
                continue;
            }
            catch (Exception e)
            {
                _logger.LogCritical("Failed to apply changes on top of previously recreated code flow: {message}", e.Message);
                throw;
            }
        }

        throw new DarcException($"Failed to apply changes due to conflicts even after {flowsToRecreate} previous flows were recreated");
    }

    /// <summary>
    /// Unwinds the last flow before previousFlows and creates a work branch there.
    /// </summary>
    /// <returns>The previous-previous flow and its previous flows.</returns>
    protected abstract Task<(Codeflow, LastFlows)> UnwindPreviousFlowAsync(
        SourceMapping mapping,
        ILocalGitRepo targetRepo,
        LastFlows previousFlows,
        string branchToCreate,
        string targetBranch,
        bool unsafeFlow,
        CancellationToken cancellationToken);

    /// <summary>
    /// Verifies whether the specified codeflow continues linearly from the previous codeflow.
    /// </summary>
    protected abstract Task EnsureCodeflowLinearityAsync(ILocalGitRepo repo, Codeflow currentFlow, LastFlows lastFlows);

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

    private async Task<IReadOnlyCollection<string>> GetChangesInHeadBranch(
        SourceMapping mapping,
        ILocalGitRepo repo,
        bool currentIsBackflow,
        string shaBeforeRecreation,
        CancellationToken cancellationToken)
    {
        ProcessExecutionResult diffResult = await _localGitClient.RunGitCommandAsync(
            currentIsBackflow ? repo.Path : _vmrInfo.VmrPath,
            ["diff", "--name-only", shaBeforeRecreation],
            cancellationToken);
        diffResult.ThrowIfFailed("Failed to diff changed files after flow re-creation");

        IReadOnlyCollection<string> changedFiles = diffResult.GetOutputLines();
        if (!currentIsBackflow)
        {
            var prefix = VmrInfo.GetRelativeRepoSourcesPath(mapping.Name).ToString();
            changedFiles = [.. changedFiles
                .Where(f => f.StartsWith(prefix, StringComparison.InvariantCultureIgnoreCase))
                .Select(f => f.Substring(prefix.Length + 1))];
        }

        return changedFiles;
    }

    /// <summary>
    /// Handles files that were reverted in the source repository and would not be included in the PR branch changes.
    /// Works around a problem that happens in the following scenario:
    /// 
    ///   repo                   VMR
    ///     O───────────────────►O 0. 
    ///     │                 2. │
    ///   1.O────────────────O   │
    ///     │                │   │
    ///     │                └──►O 3.
    ///     │                    │
    ///   4.O─────────────────x  │
    ///     │                 5. │
    ///
    /// The following happens:
    ///    0. Repo and VMR are initialized
    ///    1. Two files(`conflict.txt` and `revert.txt`) are added in the repo
    ///    2. FF is opened and in the PR branch, we change `conflict.txt` to something
    ///    3. FF PR is merged
    ///    4. The `revert.txt` file is reverted in the repo, the `conflict.txt` is changed to something else
    ///    5. The next forward flow will conflict over the `conflict.txt` file
    ///       This means the FF branch will be based on 0.
    ///       This means the FF branch needs to have all the changes from the repo(1-4)
    ///       BUT the revert.txt won't be part of the changes because it was reverted
    ///       That means the PR branch won't remove it and it will stay in the VMR (even after we resolve the conflict)
    ///
    /// This method detects such reverts and resets the file so they match the source repository.
    /// </summary>
    private async Task HandleRevertedFiles(
        CodeflowOptions codeflowOptions,
        ILocalGitRepo repo,
        string shaBeforeRecreation,
        IReadOnlyCollection<string> changedFilesAfterRecreation,
        CancellationToken cancellationToken)
    {
        bool currentIsBackflow = codeflowOptions.CurrentFlow is Backflow;
        var changedFilesAfterCurrentChanges = await GetChangesInHeadBranch(
            codeflowOptions.Mapping,
            repo,
            currentIsBackflow,
            shaBeforeRecreation,
            cancellationToken);

        // We compare changed files before and after current changes to see if a file was reverted in between
        // Such file would not appear in the PR branch changes, so we'd need to handle it ourselves
        var revertedFiles = changedFilesAfterRecreation
            .Where(f => !changedFilesAfterCurrentChanges.Contains(f))
            .ToList();

        if (revertedFiles.Count == 0)
        {
            return;
        }

        _logger.LogInformation("Reverted files detected after applying changes: {files}. Resetting the files to their current state.",
            string.Join(", ", revertedFiles));

        UnixPath vmrPrefix = VmrInfo.GetRelativeRepoSourcesPath(codeflowOptions.Mapping.Name);

        var (sourceRepo, targetRepo) = (_localGitRepoFactory.Create(_vmrInfo.VmrPath), repo);
        if (!currentIsBackflow)
        {
            (sourceRepo, targetRepo) = (targetRepo, sourceRepo);
        }

        foreach (string revertedFile in revertedFiles)
        {
            var (sourceFile, targetFile) = ((vmrPrefix / revertedFile).Path, revertedFile);
            if (!currentIsBackflow)
            {
                (sourceFile, targetFile) = (targetFile, sourceFile);
            }

            // Set the file to the current state in the source repo
            var sourceContent = await sourceRepo.GetFileFromGitAsync(sourceFile, codeflowOptions.CurrentFlow.SourceSha);
            if (sourceContent is null)
            {
                // If the file exists in the target repo, we can just remove it
                // as the source repo reverted it adding it
                if (_fileSystem.FileExists(targetRepo.Path / targetFile))
                {
                    _fileSystem.DeleteFile(targetRepo.Path / targetFile);
                    await targetRepo.StageAsync([targetFile], cancellationToken);
                    continue;
                }

                // If the file was added and then removed again in the original repo it won't exist in the head branch
                // Because the head branch is likely based on the previous flow (so before it was added in the target repo)
                // The target branch will have the file in it and we need to make sure it will get removed in the PR
                // Since the target branch and head branch will be in conflict anyway, we can leave a conflicting content
                // in the file that will hint the user to remove the file during conflict resolution.
                sourceContent = FileToBeRemovedContent;
            }

            _fileSystem.WriteToFile(targetRepo.Path / targetFile, sourceContent);
            await targetRepo.StageAsync([targetFile], cancellationToken);
        }

        var stagedFiles = await targetRepo.GetStagedFilesAsync();
        if (stagedFiles.Count > 0)
        {
            await targetRepo.CommitAsync(
                $"Fix undesired regressions that happened due to incremental codeflows at commit `{codeflowOptions.Build.Commit}`",
                allowEmpty: true,
                cancellationToken: cancellationToken);
            await repo.ResetWorkingTree();
        }
    }

    protected virtual async Task<IReadOnlyCollection<UnixPath>> MergeWorkBranchAsync(
        CodeflowOptions codeflowOptions,
        ILocalGitRepo targetRepo,
        IWorkBranch workBranch,
        bool headBranchExisted,
        string commitMessage,
        CancellationToken cancellationToken)
    {
        return await workBranch.RebaseAsync(cancellationToken);
    }

    protected abstract NativePath GetEngCommonPath(NativePath sourceRepo);
    protected abstract bool TargetRepoIsVmr();
}

/// <summary>
/// Holds information about the last flows between a repo and a VMR.
/// </summary>
/// <param name="LastFlow">Last flow from the PoV of the current commit. Equals LastBackFlow or LastForwardFlow</param>
/// <param name="LastBackFlow">Last backflow from the PoV of the current commit</param>
/// <param name="LastForwardFlow">Last forward flow from the PoV of the current commit</param>
/// <param name="CrossingFlow">A recent flow that should be taken into account as it crosses the last flow. See DetectCrossingFlow for more details.</param>
public record LastFlows(
    Codeflow LastFlow,
    Backflow? LastBackFlow,
    ForwardFlow LastForwardFlow,
    Codeflow? CrossingFlow);

/// <summary>
/// Delegate for applying latest changes with an option to enable rebase mode.
/// </summary>
/// <param name="enableRebase">When true, enables rebase mode for applying changes</param>
/// <returns>When enableRebase is true, returns a list of conflicting files</returns>
public delegate Task<CodeFlowResult> ApplyLatestChangesDelegate(bool enableRebase);
