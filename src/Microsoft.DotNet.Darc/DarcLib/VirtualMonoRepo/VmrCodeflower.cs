// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

/// <summary>
/// This class is responsible for taking changes done to a repo in the VMR and backflowing them into the repo.
/// It only makes patches/changes locally, no other effects are done.
/// </summary>
internal abstract class VmrCodeflower
{
    private readonly IVmrInfo _vmrInfo;
    private readonly ISourceManifest _sourceManifest;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly ILocalGitClient _localGitClient;
    private readonly IVersionDetailsParser _versionDetailsParser;
    private readonly IProcessManager _processManager;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<VmrCodeflower> _logger;

    protected VmrCodeflower(
        IVmrInfo vmrInfo,
        ISourceManifest sourceManifest,
        IVmrDependencyTracker dependencyTracker,
        ILocalGitClient localGitClient,
        IVersionDetailsParser versionDetailsParser,
        IProcessManager processManager,
        IFileSystem fileSystem,
        ILogger<VmrCodeflower> logger)
    {
        _vmrInfo = vmrInfo;
        _sourceManifest = sourceManifest;
        _dependencyTracker = dependencyTracker;
        _localGitClient = localGitClient;
        _versionDetailsParser = versionDetailsParser;
        _processManager = processManager;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    /// <summary>
    /// Main common entrypoint method that loads information about the last flow and calls the appropriate flow method.
    /// The algorithm is described in depth in the Unified Build documentation
    /// https://github.com/dotnet/arcade/blob/main/Documentation/UnifiedBuild/VMR-Full-Code-Flow.md#the-code-flow-algorithm
    /// </summary>
    /// <returns>Name of the PR branch that was created for the changes</returns>
    protected async Task<string?> FlowCodeAsync(
        bool isBackflow,
        NativePath repoPath,
        string mappingName,
        string shaToFlow,
        bool discardPatches,
        CancellationToken cancellationToken = default)
    {
        await _dependencyTracker.InitializeSourceMappings();
        _sourceManifest.Refresh(_vmrInfo.SourceManifestPath);

        var mapping = _dependencyTracker.Mappings.First(m => m.Name == mappingName);
        Codeflow lastFlow = await GetLastFlowAsync(mapping, repoPath, isBackflow);

        if (lastFlow.SourceSha == shaToFlow)
        {
            _logger.LogInformation("No new commits to flow from {sourceRepo}", isBackflow ? "VMR" : mapping);
            return null;
        }

        _logger.LogInformation("Last flow was {type} {sourceSha} -> {targetSha}",
            isBackflow ? "backflow" : "forward flow",
            lastFlow.SourceSha,
            lastFlow.TargetSha);

        string? branchName;
        if ((lastFlow is Backflow) == isBackflow)
        {
            _logger.LogInformation("Current flow is in the same direction");
            branchName = await SameDirectionFlowAsync(mapping, shaToFlow, repoPath, lastFlow, discardPatches, cancellationToken);
        }
        else
        {
            _logger.LogInformation("Current flow is in the opposite direction");
            branchName = await OppositeDirectionFlowAsync(mapping, shaToFlow, repoPath, lastFlow, discardPatches, cancellationToken);
        }

        if (branchName is null)
        {
            // TODO: Clean up repos?
            _logger.LogInformation("Nothing to flow from {sourceRepo}", isBackflow ? "VMR" : mapping);
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
        string shaToFlow,
        NativePath repoPath,
        Codeflow lastFlow,
        bool discardPatches,
        CancellationToken cancellationToken);

    /// <summary>
    /// Handles flowing changes that succeed a flow that was in the opposite direction (incoming in the source repo).
    /// The changes that are flown are taken from a diff of repo contents and the last sync point from the last flow.
    /// </summary>
    /// <returns>Name of the PR branch that was created for the changes</returns>
    protected abstract Task<string?> OppositeDirectionFlowAsync(
        SourceMapping mapping,
        string shaToFlow,
        NativePath repoPath,
        Codeflow lastFlow,
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
        await _localGitClient.CheckoutAsync(_vmrInfo.VmrPath, gitRef);
        await _dependencyTracker.InitializeSourceMappings();
        _sourceManifest.Refresh(_vmrInfo.SourceManifestPath);
    }

    /// <summary>
    /// Checks the last flows between a repo and a VMR and returns the most recent one.
    /// </summary>
    private async Task<Codeflow> GetLastFlowAsync(SourceMapping mapping, NativePath repoPath, bool currentIsBackflow)
    {
        ForwardFlow lastForwardFlow = await GetLastForwardFlow(mapping.Name);
        Backflow? lastBackflow = await GetLastBackflow(repoPath);

        if (lastBackflow is null)
        {
            return lastForwardFlow;
        }

        string backwardSha, forwardSha;
        NativePath sourceRepo;
        if (currentIsBackflow)
        {
            (backwardSha, forwardSha) = (lastBackflow.VmrSha, lastForwardFlow.VmrSha);
            sourceRepo = _vmrInfo.VmrPath;
        }
        else
        {
            (backwardSha, forwardSha) = (lastBackflow.RepoSha, lastForwardFlow.RepoSha);
            sourceRepo = repoPath;
        }

        GitObjectType objectType1 = await _localGitClient.GetObjectTypeAsync(sourceRepo, backwardSha);
        GitObjectType objectType2 = await _localGitClient.GetObjectTypeAsync(sourceRepo, forwardSha);

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
        var content = await _fileSystem.ReadAllTextAsync(repoPath / VersionFiles.VersionDetailsXml);
        SourceDependency? source = _versionDetailsParser.ParseVersionDetailsXml(content).Source;
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

    /// <summary>
    /// Compares 2 git commits and returns true if the first one is an ancestor of the second one.
    /// </summary>
    private async Task<bool> IsAncestorCommit(NativePath repoPath, string parent, string ancestor)
    {
        var result = await _processManager.ExecuteGit(repoPath, ["merge-base", "--is-ancestor", parent, ancestor]);

        if (!string.IsNullOrEmpty(result.StandardError))
        {
            result.ThrowIfFailed($"Failed to determine which commit of {repoPath} is older ({parent}, {ancestor})");
        }

        return result.ExitCode == 0;
    }

    protected abstract record Codeflow(string SourceSha, string TargetSha)
    {
        public abstract string RepoSha { get; init; }

        public abstract string VmrSha { get; init; }
    }

    protected record ForwardFlow(string RepoSha, string VmrSha) : Codeflow(RepoSha, VmrSha);

    protected record Backflow(string VmrSha, string RepoSha) : Codeflow(VmrSha, RepoSha);
}
