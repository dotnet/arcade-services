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
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

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
    private readonly ILocalLibGit2Client _libGit2Client;
    private readonly ILocalGitRepoFactory _localGitRepoFactory;
    private readonly IVersionDetailsParser _versionDetailsParser;
    private readonly IDependencyFileManager _dependencyFileManager;
    private readonly ICoherencyUpdateResolver _coherencyUpdateResolver;
    private readonly IAssetLocationResolver _assetLocationResolver;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<VmrCodeFlower> _logger;

    protected VmrCodeFlower(
        IVmrInfo vmrInfo,
        ISourceManifest sourceManifest,
        IVmrDependencyTracker dependencyTracker,
        ILocalGitClient localGitClient,
        ILocalLibGit2Client libGit2Client,
        ILocalGitRepoFactory localGitRepoFactory,
        IVersionDetailsParser versionDetailsParser,
        IDependencyFileManager dependencyFileManager,
        ICoherencyUpdateResolver coherencyUpdateResolver,
        IAssetLocationResolver assetLocationResolver,
        IFileSystem fileSystem,
        ILogger<VmrCodeFlower> logger)
    {
        _vmrInfo = vmrInfo;
        _sourceManifest = sourceManifest;
        _dependencyTracker = dependencyTracker;
        _localGitClient = localGitClient;
        _libGit2Client = libGit2Client;
        _localGitRepoFactory = localGitRepoFactory;
        _versionDetailsParser = versionDetailsParser;
        _dependencyFileManager = dependencyFileManager;
        _coherencyUpdateResolver = coherencyUpdateResolver;
        _assetLocationResolver = assetLocationResolver;
        _fileSystem = fileSystem;
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
        string baseBranch,
        string targetBranch,
        bool discardPatches,
        bool rebaseConflicts,
        CancellationToken cancellationToken = default)
    {
        if (lastFlow.SourceSha == currentFlow.TargetSha)
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
                baseBranch,
                targetBranch,
                discardPatches,
                rebaseConflicts,
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
                baseBranch,
                targetBranch,
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
    /// <param name="baseBranch">If target branch does not exist, it is created off of this branch</param>
    /// <param name="targetBranch">Target branch to make the changes on</param>
    /// <param name="discardPatches">If true, patches are deleted after applying them</param>
    /// <param name="rebaseConflicts">When a conflict is found, should we retry the flow from an earlier checkpoint?</param>
    /// <returns>True if there were changes to flow</returns>
    protected abstract Task<bool> SameDirectionFlowAsync(
        SourceMapping mapping,
        Codeflow lastFlow,
        Codeflow currentFlow,
        ILocalGitRepo repo,
        Build build,
        IReadOnlyCollection<string>? excludedAssets,
        string baseBranch,
        string targetBranch,
        bool discardPatches,
        bool rebaseConflicts,
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
    /// <param name="baseBranch">If target branch does not exist, it is created off of this branch</param>
    /// <param name="targetBranch">Target branch to make the changes on</param>
    /// <param name="discardPatches">If true, patches are deleted after applying them</param>
    /// <returns>True if there were changes to flow</returns>
    protected abstract Task<bool> OppositeDirectionFlowAsync(
        SourceMapping mapping,
        Codeflow lastFlow,
        Codeflow currentFlow,
        ILocalGitRepo repo,
        Build build,
        string baseBranch,
        string targetBranch,
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
    /// Updates version details, eng/common and other version files (global.json, ...) based on a build that is being flown.
    /// For backflows, updates the Source element in Version.Details.xml.
    /// </summary>
    /// <param name="sourceRepo">Source repository (needed when eng/common is flown too)</param>
    /// <param name="targetRepo">Target repository directory</param>
    /// <param name="build">Build with assets (dependencies) that is being flows</param>
    /// <param name="excludedAssets">Assets to exclude from the dependency flow</param>
    /// <param name="sourceElementSha">For backflows, VMR SHA that is being flown so it can be stored in Version.Details.xml</param>
    protected async Task<bool> UpdateDependenciesAndToolset(
        NativePath sourceRepo,
        ILocalGitRepo targetRepo,
        Build build,
        IReadOnlyCollection<string>? excludedAssets,
        string? sourceElementSha,
        CancellationToken cancellationToken)
    {
        string versionDetailsXml = await targetRepo.GetFileFromGitAsync(VersionFiles.VersionDetailsXml)
            ?? throw new Exception($"Failed to read {VersionFiles.VersionDetailsXml} from {targetRepo.Path} (file does not exist)");
        VersionDetails versionDetails = _versionDetailsParser.ParseVersionDetailsXml(versionDetailsXml);
        await _assetLocationResolver.AddAssetLocationToDependenciesAsync(versionDetails.Dependencies);

        SourceDependency? sourceOrigin = null;
        List<DependencyUpdate> updates;
        bool hadUpdates = false;

        if (sourceElementSha != null)
        {
            sourceOrigin = new SourceDependency(
                build.GetRepository(),
                sourceElementSha,
                build.Id);

            if (versionDetails.Source?.Sha != sourceElementSha)
            {
                hadUpdates = true;
            }
        }

        // Generate the <Source /> element and get updates
        if (build is not null)
        {
            IEnumerable<AssetData> assetData = build.Assets
                .Where(a => excludedAssets is null || !excludedAssets.Contains(a.Name))
                .Select(a => new AssetData(a.NonShipping)
                {
                    Name = a.Name,
                    Version = a.Version
                });

            updates = _coherencyUpdateResolver.GetRequiredNonCoherencyUpdates(
                build.GetRepository() ?? Constants.DefaultVmrUri,
                build.Commit,
                assetData,
                versionDetails.Dependencies);

            await _assetLocationResolver.AddAssetLocationToDependenciesAsync([.. updates.Select(u => u.To)]);
        }
        else
        {
            updates = [];
        }

        // If we are updating the arcade sdk we need to update the eng/common files as well
        DependencyDetail? arcadeItem = updates.GetArcadeUpdate();
        SemanticVersion? targetDotNetVersion = null;

        if (arcadeItem != null)
        {
            targetDotNetVersion = await _dependencyFileManager.ReadToolsDotnetVersionAsync(arcadeItem.RepoUri, arcadeItem.Commit);
        }

        GitFileContentContainer updatedFiles = await _dependencyFileManager.UpdateDependencyFiles(
            updates.Select(u => u.To),
            sourceOrigin,
            targetRepo.Path,
            Constants.HEAD,
            versionDetails.Dependencies,
            targetDotNetVersion);

        // TODO https://github.com/dotnet/arcade-services/issues/3251: Stop using LibGit2SharpClient for this
        await _libGit2Client.CommitFilesAsync(updatedFiles.GetFilesToCommit(), targetRepo.Path, null, null);

        // Update eng/common files
        if (arcadeItem != null)
        {
            var commonDir = targetRepo.Path / Constants.CommonScriptFilesPath;
            if (_fileSystem.DirectoryExists(commonDir))
            {
                _fileSystem.DeleteDirectory(commonDir, true);
            }

            _fileSystem.CopyDirectory(
                sourceRepo / Constants.CommonScriptFilesPath,
                targetRepo.Path / Constants.CommonScriptFilesPath,
                true);
        }

        if (!await targetRepo.HasWorkingTreeChangesAsync())
        {
            return hadUpdates;
        }

        await targetRepo.StageAsync(["."], cancellationToken);

        // TODO: Better commit message?
        await targetRepo.CommitAsync("Updated dependencies", allowEmpty: true, cancellationToken: cancellationToken);
        return true;
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

    protected abstract record Codeflow(string SourceSha, string TargetSha)
    {
        public abstract string RepoSha { get; init; }

        public abstract string VmrSha { get; init; }

        public string GetBranchName() => $"darc/{Name}/{Commit.GetShortSha(SourceSha)}-{Commit.GetShortSha(TargetSha)}";

        public abstract string Name { get; }
    }

    protected record ForwardFlow(string RepoSha, string VmrSha) : Codeflow(RepoSha, VmrSha)
    {
        public override string Name { get; } = "forward";
    }

    protected record Backflow(string VmrSha, string RepoSha) : Codeflow(VmrSha, RepoSha)
    {
        public override string Name { get; } = "back";
    }
}
