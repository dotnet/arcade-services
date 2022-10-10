// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.IO;
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
        """
        [{name}] Initial pull of the individual repository ({newShaShort})

        Original commit: {remote}/commit/{newSha}
        """;
    
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly IVmrPatchHandler _patchHandler;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<VmrUpdater> _logger;

    private readonly string _tmpPath;

    public VmrInitializer(
        IVmrDependencyTracker dependencyTracker,
        IVmrPatchHandler patchHandler,
        IProcessManager processManager,
        IRemoteFactory remoteFactory,
        ILocalGitRepo localGitRepo,
        IVersionDetailsParser versionDetailsParser,
        IFileSystem fileSystem,
        ILogger<VmrUpdater> logger,
        IVmrManagerConfiguration configuration)
        : base(dependencyTracker, processManager, remoteFactory, localGitRepo, versionDetailsParser, logger, configuration.TmpPath)
    {
        _dependencyTracker = dependencyTracker;
        _patchHandler = patchHandler;
        _fileSystem = fileSystem;
        _logger = logger;
        _tmpPath = configuration.TmpPath;
    }

    public async Task InitializeRepository(
        SourceMapping mapping,
        string? targetRevision,
        string? targetVersion,
        bool initializeDependencies,
        CancellationToken cancellationToken)
    {
        var reposToUpdate = new Queue<(SourceMapping mapping, string? targetRevision, string? targetVersion)>();
        reposToUpdate.Enqueue((mapping, targetRevision, targetVersion));

        while (reposToUpdate.TryDequeue(out var repoToUpdate))
        {
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

                    if (_fileSystem.DirectoryExists(_dependencyTracker.GetRepoSourcesPath(dependencyMapping)))
                    {
                        // Repository has already been initialized
                        continue;
                    }

                    _logger.LogDebug("Detected dependency of {parent} - {repo} / {commit} ({version})",
                        mapping.Name,
                        dependencyMapping.Name,
                        dependency.Commit,
                        dependency.Version);

                    reposToUpdate.Enqueue((dependencyMapping, dependency.Commit, dependency.Version));
                }
            }
        }
    }

    private async Task InitializeRepository(
        SourceMapping mapping,
        string? targetRevision,
        string? targetVersion,
        CancellationToken cancellationToken)
    {
        if (_dependencyTracker.GetDependencyVersion(mapping) is not null)
        {
            throw new EmptySyncException($"Repository {mapping.Name} already exists");
        }

        _logger.LogInformation("Initializing {name} at {revision}..", mapping.Name, targetRevision ?? mapping.DefaultRef);

        string clonePath = await CloneOrPull(mapping);
        cancellationToken.ThrowIfCancellationRequested();

        using var clone = new Repository(clonePath);
        var commit = GetCommit(clone, (targetRevision is null || targetRevision == HEAD) ? null : targetRevision);

        var patches = await _patchHandler.CreatePatches(
            mapping,
            clonePath,
            Constants.EmptyGitObject,
            commit.Id.Sha,
            _tmpPath,
            _tmpPath,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var patch in patches)
        {
            await _patchHandler.ApplyPatch(mapping, patch, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }

        _dependencyTracker.UpdateDependencyVersion(mapping, new(commit.Id.Sha, targetVersion));
        Commands.Stage(new Repository(_dependencyTracker.VmrPath), VmrDependencyTracker.GitInfoSourcesDir);
        cancellationToken.ThrowIfCancellationRequested();

        await _patchHandler.ApplyVmrPatches(mapping, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // Commit but do not add files (they were added to index directly)
        var message = PrepareCommitMessage(InitializationCommitMessage, mapping, newSha: commit.Id.Sha);
        Commit(message, DotnetBotCommitSignature);

        _logger.LogInformation("Initialization of {name} finished", mapping.Name);
    }
}
