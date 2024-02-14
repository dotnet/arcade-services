﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.Maestro.Client.Models;
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
    private readonly IVersionDetailsParser _versionDetailsParser;
    private readonly IDependencyFileManager _dependencyFileManager;
    private readonly ICoherencyUpdateResolver _coherencyUpdateResolver;
    private readonly IAssetLocationResolver _assetLocationResolver;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<VmrCodeFlower> _logger;

    protected ILocalGitRepo LocalVmr { get; }

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
        _versionDetailsParser = versionDetailsParser;
        _dependencyFileManager = dependencyFileManager;
        _coherencyUpdateResolver = coherencyUpdateResolver;
        _assetLocationResolver = assetLocationResolver;
        _fileSystem = fileSystem;
        _logger = logger;

        LocalVmr = localGitRepoFactory.Create(_vmrInfo.VmrPath);
    }

    /// <summary>
    /// Main common entrypoint method that loads information about the last flow and calls the appropriate flow method.
    /// The algorithm is described in depth in the Unified Build documentation
    /// https://github.com/dotnet/arcade/blob/main/Documentation/UnifiedBuild/VMR-Full-Code-Flow.md#the-code-flow-algorithm
    /// </summary>
    /// <returns>Name of the PR branch that was created for the changes</returns>
    protected async Task<string?> FlowCodeAsync(
        Codeflow lastFlow,
        Codeflow currentFlow,
        ILocalGitRepo repo,
        SourceMapping mapping,
        Build? build,
        bool discardPatches,
        CancellationToken cancellationToken = default)
    {
        if (lastFlow.SourceSha == currentFlow.TargetSha)
        {
            _logger.LogInformation("No new commits to flow from {sourceRepo}", currentFlow is Backflow ? "VMR" : mapping.Name);
            return null;
        }

        _logger.LogInformation("Last flow was {type} flow: {sourceSha} -> {targetSha}",
            currentFlow.Name,
            lastFlow.SourceSha,
            lastFlow.TargetSha);

        string? branchName;
        if (lastFlow.Name == currentFlow.Name)
        {
            _logger.LogInformation("Current flow is in the same direction");
            branchName = await SameDirectionFlowAsync(mapping, lastFlow, currentFlow, repo, build, discardPatches, cancellationToken);
        }
        else
        {
            _logger.LogInformation("Current flow is in the opposite direction");
            branchName = await OppositeDirectionFlowAsync(mapping, lastFlow, currentFlow, repo, build, discardPatches, cancellationToken);
        }

        if (branchName is null)
        {
            // TODO: Clean up repos?
            _logger.LogInformation("Nothing to flow from {sourceRepo}", currentFlow is Backflow ? "VMR" : mapping.Name);
        }

        return branchName;
    }

    /// <summary>
    /// Handles flowing changes that succeed a flow that was in the same direction (outgoing from the source repo).
    /// The changes that are flown are taken from a simple patch of changes that occurred since the last flow.
    /// </summary>
    /// <returns>Name of the PR branch that was created for the changes</returns>
    protected abstract Task<string?> SameDirectionFlowAsync(
        SourceMapping mapping,
        Codeflow lastFlow,
        Codeflow currentFlow,
        ILocalGitRepo repo,
        Build? build,
        bool discardPatches,
        CancellationToken cancellationToken);

    /// <summary>
    /// Handles flowing changes that succeed a flow that was in the opposite direction (incoming in the source repo).
    /// The changes that are flown are taken from a diff of repo contents and the last sync point from the last flow.
    /// </summary>
    /// <returns>Name of the PR branch that was created for the changes</returns>
    protected abstract Task<string?> OppositeDirectionFlowAsync(
        SourceMapping mapping,
        Codeflow lastFlow,
        Codeflow currentFlow,
        ILocalGitRepo repo,
        Build? build,
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
    /// Checks out a given git ref in the VMR and refreshes the VMR-related information.
    /// </summary>
    protected async Task CheckOutVmr(string gitRef)
    {
        await LocalVmr.CheckoutAsync(gitRef);
        await _dependencyTracker.InitializeSourceMappings();
        _sourceManifest.Refresh(_vmrInfo.SourceManifestPath);
    }

    /// <summary>
    /// Checks the last flows between a repo and a VMR and returns the most recent one.
    /// </summary>
    protected async Task<Codeflow> GetLastFlowAsync(SourceMapping mapping, ILocalGitRepo repoClone, bool currentIsBackflow)
    {
        await _dependencyTracker.InitializeSourceMappings();
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
            sourceRepo = LocalVmr;
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

        // Let's determine the last flow by comparing source commit of last backflow with target commit of last forward flow
        bool isForwardOlder = await IsAncestorCommit(sourceRepo, forwardSha, backwardSha);
        bool isBackwardOlder = await IsAncestorCommit(sourceRepo, backwardSha, forwardSha);

        // Commits not comparable
        if (isBackwardOlder == isForwardOlder)
        {
            // TODO: Figure out when this can happen and what to do about it
            throw new Exception($"Failed to determine which commit of {sourceRepo} is older ({lastForwardFlow.VmrSha}, {lastBackflow.VmrSha})");
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
        IVersionedSourceComponent repoInVmr = _sourceManifest.Repositories.FirstOrDefault(r => r.Path == mappingName)
            ?? throw new ArgumentException($"No repository mapping named {mappingName} found");

        // Last forward flow SHAs come from source-manifest.json in the VMR
        string lastForwardRepoSha = repoInVmr.CommitSha;
        string lastForwardVmrSha = await BlameLineAsync(
            _vmrInfo.SourceManifestPath,
            line => line.Contains(lastForwardRepoSha));

        return new ForwardFlow(lastForwardRepoSha, lastForwardVmrSha);
    }

    protected async Task UpdateDependenciesAndToolset(
        ILocalGitRepo targetRepo,
        Build? build,
        string currentVmrSha,
        bool updateSourceElement,
        CancellationToken cancellationToken)
    {
        string versionDetailsXml = await targetRepo.GetFileFromGitAsync(VersionFiles.VersionDetailsXml)
            ?? throw new Exception($"Failed to read {VersionFiles.VersionDetailsXml} from {targetRepo.Path}");
        VersionDetails versionDetails = _versionDetailsParser.ParseVersionDetailsXml(versionDetailsXml);
        await _assetLocationResolver.AddAssetLocationToDependenciesAsync(versionDetails.Dependencies);

        SourceDependency? sourceOrigin;
        List<DependencyUpdate> updates;

        if (updateSourceElement)
        {
            sourceOrigin = new SourceDependency(
                build?.GitHubRepository ?? build?.AzureDevOpsRepository ?? Constants.DefaultVmrUri,
                currentVmrSha);
        }
        else
        {
            sourceOrigin = versionDetails.Source != null
                ? versionDetails.Source with { Sha = currentVmrSha }
                : new SourceDependency(Constants.DefaultVmrUri, currentVmrSha); // First ever backflow for the repo
        }

        // Generate the <Source /> element and get updates
        if (build is not null)
        {
            IEnumerable<AssetData> assetData = build.Assets.Select(
                a => new AssetData(a.NonShipping)
                {
                    Name = a.Name,
                    Version = a.Version
                });

            updates = _coherencyUpdateResolver.GetRequiredNonCoherencyUpdates(
                sourceOrigin.Uri,
                sourceOrigin.Sha,
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
                _vmrInfo.VmrPath / Constants.CommonScriptFilesPath,
                targetRepo.Path / Constants.CommonScriptFilesPath,
                true);
        }

        await targetRepo.StageAsync(["."], cancellationToken);
        await targetRepo.CommitAsync($"Update dependency files to {currentVmrSha}", allowEmpty: true, cancellationToken: cancellationToken);
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

        public string GetBranchName() => $"codeflow/{Name}/{Commit.GetShortSha(SourceSha)}-{Commit.GetShortSha(TargetSha)}";

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
