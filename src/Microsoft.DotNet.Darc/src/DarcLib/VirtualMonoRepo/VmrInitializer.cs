// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.IO;
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
    private readonly ILogger<VmrUpdater> _logger;

    private readonly string _tmpPath;

    public VmrInitializer(
        IVmrDependencyTracker dependencyTracker,
        IVmrPatchHandler patchHandler,
        IProcessManager processManager,
        IRemoteFactory remoteFactory,
        ILocalGitRepo localGitRepo,
        IVersionDetailsParser versionDetailsParser,
        ILogger<VmrUpdater> logger,
        IVmrManagerConfiguration configuration)
        : base(dependencyTracker, processManager, remoteFactory, localGitRepo, versionDetailsParser, logger, configuration.TmpPath)
    {
        _dependencyTracker = dependencyTracker;
        _patchHandler = patchHandler;
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

        if (initializeDependencies)
        {
            await InitializeDependencies(mapping, cancellationToken);
        }
    }

    private async Task InitializeDependencies(SourceMapping mapping, CancellationToken cancellationToken)
    {
        foreach (var (dependency, dependencyMapping) in await GetDependencies(mapping, cancellationToken))
        {
            if (Directory.Exists(_dependencyTracker.GetRepoSourcesPath(dependencyMapping)))
            {
                _logger.LogDebug("Dependency {repo} has already been initialized", dependencyMapping.Name);
                continue;
            }

            _logger.LogInformation("Recursively initializing dependency {repo} / {commit} ({version})",
                dependencyMapping.Name,
                dependency.Commit,
                dependency.Version);

            await InitializeRepository(dependencyMapping, dependency.Commit, dependency.Version, true, cancellationToken);
        }
    }
}
