// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Maestro.Common;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

/// <summary>
/// This class is able to initialize an individual repository within the VMR for the first time.
/// It pulls in the new sources adhering to cloaking rules, accommodating for patched files, resolving submodules.
/// It can also initialize all other repositories recursively based on the dependencies stored in Version.Details.xml.
/// </summary>
public class VmrInitializer : VmrManagerBase, IVmrInitializer
{
    // Message shown when initializing an individual repo for the first time
    private const string InitializationCommitMessage =
        $$"""
        [{name}] Initial pull of the individual repository ({newShaShort})

        Original commit: {remote}/commit/{newSha}

        {{Constants.AUTOMATION_COMMIT_TAG}}
        """;

    // Message used when finalizing the initialization with a merge commit
    private const string MergeCommitMessage =
        $$"""
        Recursive initialization for {name} / {newShaShort}

        {{Constants.AUTOMATION_COMMIT_TAG}}
        """;

    private readonly IVmrInfo _vmrInfo;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly IVersionDetailsParser _versionDetailsParser;
    private readonly IRepositoryCloneManager _cloneManager;
    private readonly IDependencyFileManager _dependencyFileManager;
    private readonly IWorkBranchFactory _workBranchFactory;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<VmrUpdater> _logger;
    private readonly ISourceManifest _sourceManifest;

    public VmrInitializer(
        IVmrDependencyTracker dependencyTracker,
        IVmrPatchHandler patchHandler,
        IVersionDetailsParser versionDetailsParser,
        IRepositoryCloneManager cloneManager,
        IThirdPartyNoticesGenerator thirdPartyNoticesGenerator,
        ICodeownersGenerator codeownersGenerator,
        ICredScanSuppressionsGenerator credScanSuppressionsGenerator,
        ILocalGitClient localGitClient,
        ILocalGitRepoFactory localGitRepoFactory,
        IDependencyFileManager dependencyFileManager,
        IWorkBranchFactory workBranchFactory,
        IFileSystem fileSystem,
        ILogger<VmrUpdater> logger,
        ISourceManifest sourceManifest,
        IVmrInfo vmrInfo)
        : base(vmrInfo, dependencyTracker, patchHandler, thirdPartyNoticesGenerator, codeownersGenerator, credScanSuppressionsGenerator, localGitClient, localGitRepoFactory, logger)
    {
        _vmrInfo = vmrInfo;
        _dependencyTracker = dependencyTracker;
        _versionDetailsParser = versionDetailsParser;
        _cloneManager = cloneManager;
        _dependencyFileManager = dependencyFileManager;
        _workBranchFactory = workBranchFactory;
        _fileSystem = fileSystem;
        _logger = logger;
        _sourceManifest = sourceManifest;
    }

    public async Task InitializeRepository(
        string mappingName,
        string? targetRevision,
        LocalPath sourceMappingsPath,
        CodeFlowParameters codeFlowParameters,
        CancellationToken cancellationToken)
    {
        await _dependencyTracker.RefreshMetadataAsync(sourceMappingsPath);
        var mapping = _dependencyTracker.GetMapping(mappingName);

        if (_dependencyTracker.GetDependencyVersion(mapping) is not null)
        {
            throw new EmptySyncException($"Repository {mapping.Name} already exists");
        }

        var workBranchName = $"init/{mapping.Name}";
        if (targetRevision != null)
        {
            workBranchName += $"/{targetRevision}";
        }

        IWorkBranch workBranch = await _workBranchFactory.CreateWorkBranchAsync(GetLocalVmr(), workBranchName);

        var update = new VmrDependencyUpdate(
            mapping,
            mapping.DefaultRemote,
            targetRevision ?? mapping.DefaultRef,
            Parent: null,
            OfficialBuildId: null,
            BarId: null);

        try
        {
            var sourcesPath = _vmrInfo.GetRepoSourcesPath(update.Mapping);
            if (_fileSystem.DirectoryExists(sourcesPath)
                && _fileSystem.GetFiles(sourcesPath).Length > 1
                && _dependencyTracker.GetDependencyVersion(update.Mapping) == null)
            {
                throw new InvalidOperationException(
                    $"Sources for {update.Mapping.Name} already exists but repository is not initialized properly. " +
                     "Please investigate!");
            }

            await InitializeRepository(
                update,
                codeFlowParameters,
                cancellationToken);
        }
        catch (Exception)
        {
            _logger.LogWarning(
                InterruptedSyncExceptionMessage,
                workBranch.OriginalBranchName.StartsWith("sync") || workBranch.OriginalBranchName.StartsWith("init") ?
                "the original" : workBranch.OriginalBranchName);
            throw;
        }

        string newSha = _dependencyTracker.GetDependencyVersion(mapping)?.Sha
            ?? throw new Exception($"Repository {mapping.Name} was supposed to be but has not been initialized! " +
                                    "Please make sure the sources folder is empty!");

        var commitMessage = PrepareCommitMessage(MergeCommitMessage, mapping.Name, mapping.DefaultRemote, oldSha: null, newSha);
        await workBranch.MergeBackAsync(commitMessage);

        _logger.LogInformation("Recursive initialization for {repo} / {sha} finished", mapping.Name, newSha);
    }

    private async Task InitializeRepository(
        VmrDependencyUpdate update,
        CodeFlowParameters codeFlowParameters,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initializing {name} at {revision}..", update.Mapping.Name, update.TargetRevision);

        var remotes = codeFlowParameters.AdditionalRemotes
            .Where(r => r.Mapping == update.Mapping.Name)
            .Select(r => r.RemoteUri)
            .Prepend(update.RemoteUri)
            .ToArray();

        ILocalGitRepo clone = await _cloneManager.PrepareCloneAsync(
            update.Mapping,
            remotes,
            new[] { update.TargetRevision },
            update.TargetRevision,
            resetToRemote: false,
            cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        update = update with
        {
            TargetRevision = await clone.GetShaForRefAsync(update.TargetRevision)
        };

        string commitMessage = PrepareCommitMessage(InitializationCommitMessage, update.Mapping.Name, update.RemoteUri, newSha: update.TargetRevision);

        await UpdateRepoToRevisionAsync(
            update,
            clone,
            Constants.EmptyGitObject,
            commitMessage,
            restoreVmrPatches: false,
            keepConflicts: false,
            codeFlowParameters,
            cancellationToken: cancellationToken);

        _logger.LogInformation("Initialization of {name} finished", update.Mapping.Name);
    }

    /// <summary>
    /// Recursively parses Version.Details.xml files of all repositories and returns the list of source build dependencies.
    /// </summary>
    private async Task<IEnumerable<VmrDependencyUpdate>> GetAllDependenciesAsync(
        VmrDependencyUpdate root,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        CancellationToken cancellationToken)
    {
        var transitiveDependencies = new Dictionary<SourceMapping, VmrDependencyUpdate>
        {
            { root.Mapping, root },
        };

        var reposToScan = new Queue<VmrDependencyUpdate>();
        reposToScan.Enqueue(transitiveDependencies.Values.Single());

        _logger.LogInformation("Finding transitive dependencies for {mapping}:{revision}..", root.Mapping.Name, root.TargetRevision);

        while (reposToScan.TryDequeue(out var repo))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var remotes = additionalRemotes
                .Where(r => r.Mapping == repo.Mapping.Name)
                .Select(r => r.RemoteUri)
                .Append(repo.RemoteUri)
                .Prepend(repo.Mapping.DefaultRemote)
                .Distinct()
                .OrderRemotesByLocalPublicOther();

            IEnumerable<DependencyDetail>? repoDependencies = null;
            foreach (var remoteUri in remotes)
            {
                try
                {
                    repoDependencies = (await GetRepoDependenciesAsync(remoteUri, repo.TargetRevision))
                        .Where(dep => dep.SourceBuild is not null);
                    break;
                }
                catch
                {
                    _logger.LogDebug("Could not find {file} for {mapping}:{revision} in {remote}",
                        VersionFiles.VersionDetailsXml,
                        repo.Mapping.Name,
                        repo.TargetRevision,
                        remoteUri);
                }
            }

            if (repoDependencies is null)
            {
                _logger.LogInformation(
                    "Repository {repository} does not have {file} file, skipping dependency detection.",
                    repo.Mapping.Name,
                    VersionFiles.VersionDetailsXml);
                continue;
            }

            foreach (var dependency in repoDependencies)
            {
                if (!_dependencyTracker.TryGetMapping(dependency.SourceBuild.RepoName, out var mapping))
                {
                    throw new InvalidOperationException(
                        $"No source mapping named '{dependency.SourceBuild.RepoName}' found " +
                        $"for a {VersionFiles.VersionDetailsXml} dependency of {dependency.Name}");
                }

                var update = new VmrDependencyUpdate(
                    mapping,
                    dependency.RepoUri,
                    dependency.Commit,
                    repo.Mapping,
                    OfficialBuildId: null,
                    BarId: null);

                if (transitiveDependencies.TryAdd(mapping, update))
                {
                    _logger.LogDebug("Detected {parent}'s dependency {name} ({uri} / {sha})",
                        repo.Mapping.Name,
                        update.Mapping.Name,
                        update.RemoteUri,
                        update.TargetRevision);

                    reposToScan.Enqueue(update);
                }
            }
        }

        _logger.LogInformation("Found {count} transitive dependencies for {mapping}:{revision}..",
            transitiveDependencies.Count,
            root.Mapping.Name,
            root.TargetRevision);

        return transitiveDependencies.Values;
    }

    private async Task<IEnumerable<DependencyDetail>> GetRepoDependenciesAsync(string remoteRepoUri, string commitSha)
    {
        // Check if we have the file locally
        var localVersion = _sourceManifest.Repositories.FirstOrDefault(repo => repo.RemoteUri == remoteRepoUri);
        if (localVersion?.CommitSha == commitSha)
        {
            var path = _vmrInfo.VmrPath / VmrInfo.SourcesDir / localVersion.Path / VersionFiles.VersionDetailsXml;
            var content = await _fileSystem.ReadAllTextAsync(path);
            return _versionDetailsParser.ParseVersionDetailsXml(content, includePinned: true).Dependencies;
        }

        VersionDetails versionDetails = await _dependencyFileManager.ParseVersionDetailsXmlAsync(remoteRepoUri, commitSha, includePinned: true);
        return versionDetails.Dependencies;
    }
}
