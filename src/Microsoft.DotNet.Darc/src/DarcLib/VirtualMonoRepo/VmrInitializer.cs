// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LibGit2Sharp;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;

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

        {{AUTOMATION_COMMIT_TAG}}
        """;

    // Message used when finalizing the initialization with a merge commit
    private const string MergeCommitMessage =
        $$"""
        Recursive initialization for {name} / {newShaShort}

        {{AUTOMATION_COMMIT_TAG}}
        """;

    private readonly IVmrInfo _vmrInfo;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly IVmrPatchHandler _patchHandler;
    private readonly IRepositoryCloneManager _cloneManager;
    private readonly IReadmeComponentListGenerator _readmeComponentListGenerator;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<VmrUpdater> _logger;

    private readonly LocalPath _tmpPath;

    public VmrInitializer(
        IVmrDependencyTracker dependencyTracker,
        IVmrPatchHandler patchHandler,
        IVersionDetailsParser versionDetailsParser,
        IRepositoryCloneManager cloneManager,
        IThirdPartyNoticesGenerator thirdPartyNoticesGenerator,
        IReadmeComponentListGenerator readmeComponentListGenerator,
        ILocalGitRepo localGitClient,
        IFileSystem fileSystem,
        ILogger<VmrUpdater> logger,
        IVmrInfo vmrInfo)
        : base(vmrInfo, dependencyTracker, versionDetailsParser, thirdPartyNoticesGenerator, localGitClient, logger)
    {
        _vmrInfo = vmrInfo;
        _dependencyTracker = dependencyTracker;
        _patchHandler = patchHandler;
        _cloneManager = cloneManager;
        _readmeComponentListGenerator = readmeComponentListGenerator;
        _fileSystem = fileSystem;
        _logger = logger;
        _tmpPath = vmrInfo.TmpPath;
    }

    public async Task InitializeRepository(
        SourceMapping mapping,
        string? targetRevision,
        string? targetVersion,
        bool initializeDependencies,
        CancellationToken cancellationToken)
    {
        if (_dependencyTracker.GetDependencyVersion(mapping) is not null)
        {
            throw new EmptySyncException($"Repository {mapping.Name} already exists");
        }

        var workBranch = CreateWorkBranch($"init/{mapping.Name}{(targetRevision != null ? $"/{targetRevision}" : string.Empty)}");

        var reposToUpdate = new Queue<(SourceMapping mapping, string? targetRevision, string? targetVersion)>();
        reposToUpdate.Enqueue((mapping, targetRevision, targetVersion));

        while (reposToUpdate.TryDequeue(out var repoToUpdate))
        {
            if (_fileSystem.DirectoryExists(_vmrInfo.GetRepoSourcesPath(repoToUpdate.mapping)))
            {
                // Repository has already been initialized
                continue;
            }

            await InitializeRepository(repoToUpdate.mapping, repoToUpdate.targetRevision, repoToUpdate.targetVersion, cancellationToken);

            // When initializing dependencies, we initialize always to the first version of the dependency we've seen
            if (initializeDependencies)
            {
                var dependencies = await GetDependencies(repoToUpdate.mapping, cancellationToken);
                foreach (var (dependency, dependencyMapping) in dependencies)
                {
                    if (reposToUpdate.Any(r => r.mapping.Name == dependency.Name))
                    {
                        // Repository is already queued for update, we prefer that version first
                        continue;
                    }

                    if (_fileSystem.DirectoryExists(_vmrInfo.GetRepoSourcesPath(dependencyMapping)))
                    {
                        // Repository has already been initialized
                        continue;
                    }

                    _logger.LogInformation("Detected dependency of {parent} - {repo} / {commit} ({version})",
                        mapping.Name,
                        dependencyMapping.Name,
                        dependency.Commit,
                        dependency.Version);

                    reposToUpdate.Enqueue((dependencyMapping, dependency.Commit, dependency.Version));
                }
            }
        }

        string newSha = _dependencyTracker.GetDependencyVersion(mapping)!.Sha;

        var commitMessage = PrepareCommitMessage(MergeCommitMessage, mapping, oldSha: null, newSha);
        workBranch.MergeBack(commitMessage);

        _logger.LogInformation("Recursive initialization for {repo} / {sha} finished", mapping.Name, newSha);
    }

    private async Task InitializeRepository(
        SourceMapping mapping,
        string? targetRevision,
        string? targetVersion,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initializing {name} at {revision}..", mapping.Name, targetRevision ?? mapping.DefaultRef);

        var clonePath = await _cloneManager.PrepareClone(mapping.DefaultRemote, targetRevision ?? mapping.DefaultRef, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        string commitSha = GetShaForRef(clonePath, (targetRevision is null || targetRevision == HEAD) ? null : targetRevision);

        var patches = await _patchHandler.CreatePatches(
            mapping,
            clonePath,
            Constants.EmptyGitObject,
            commitSha,
            _tmpPath,
            _tmpPath,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var patch in patches)
        {
            await _patchHandler.ApplyPatch(mapping, patch, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }

        _dependencyTracker.UpdateDependencyVersion(mapping, new(commitSha, targetVersion));
        await _readmeComponentListGenerator.UpdateReadme();
        Commands.Stage(new Repository(_vmrInfo.VmrPath), new string[]
        { 
            VmrInfo.ReadmeFileName,
            VmrInfo.GitInfoSourcesDir,
            _vmrInfo.GetSourceManifestPath() 
        });

        cancellationToken.ThrowIfCancellationRequested();

        await ApplyVmrPatches(mapping, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        
        await UpdateThirdPartyNotices(cancellationToken);

        // Commit but do not add files (they were added to index directly)
        var message = PrepareCommitMessage(InitializationCommitMessage, mapping, newSha: commitSha);
        Commit(message, DotnetBotCommitSignature);

        _logger.LogInformation("Initialization of {name} finished", mapping.Name);
    }

    /// <summary>
    /// Applies VMR patches onto files of given mapping's subrepository.
    /// These files are stored in the VMR and applied on top of the individual repos.
    /// </summary>
    private async Task ApplyVmrPatches(SourceMapping mapping, CancellationToken cancellationToken)
    {
        var vmrPatches = _patchHandler.GetVmrPatches(mapping);
        if (!vmrPatches.Any())
        {
            return;
        }

        _logger.LogInformation("Applying VMR patches for {mappingName}..", mapping.Name);

        foreach (var patchFile in vmrPatches)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogDebug("Applying {patch}..", patchFile);
            await _patchHandler.ApplyPatch(mapping, patchFile, cancellationToken);
        }
    }
}
