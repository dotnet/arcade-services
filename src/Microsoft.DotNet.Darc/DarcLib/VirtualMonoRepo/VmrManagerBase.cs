// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public abstract class VmrManagerBase
{
    protected const string InterruptedSyncExceptionMessage = 
        "A new branch was created for the sync and didn't get merged as the sync " +
        "was interrupted. A new sync should start from {original} branch.";

    private readonly IVmrInfo _vmrInfo;
    private readonly ISourceManifest _sourceManifest;
    private readonly IVmrDependencyTracker _dependencyInfo;
    private readonly IVmrPatchHandler _patchHandler;
    private readonly IVersionDetailsParser _versionDetailsParser;
    private readonly IThirdPartyNoticesGenerator _thirdPartyNoticesGenerator;
    private readonly IComponentListGenerator _componentListGenerator;
    private readonly ICodeownersGenerator _codeownersGenerator;
    private readonly ILocalGitClient _localGitClient;
    private readonly IDependencyFileManager _dependencyFileManager;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger _logger;

    protected ILocalGitRepo LocalVmr { get; }

    protected VmrManagerBase(
        IVmrInfo vmrInfo,
        ISourceManifest sourceManifest,
        IVmrDependencyTracker dependencyInfo,
        IVmrPatchHandler vmrPatchHandler,
        IVersionDetailsParser versionDetailsParser,
        IThirdPartyNoticesGenerator thirdPartyNoticesGenerator,
        IComponentListGenerator componentListGenerator,
        ICodeownersGenerator codeownersGenerator,
        ILocalGitClient localGitClient,
        ILocalGitRepoFactory localGitRepoFactory,
        IDependencyFileManager dependencyFileManager,
        IFileSystem fileSystem,
        ILogger<VmrUpdater> logger)
    {
        _logger = logger;
        _vmrInfo = vmrInfo;
        _sourceManifest = sourceManifest;
        _dependencyInfo = dependencyInfo;
        _patchHandler = vmrPatchHandler;
        _versionDetailsParser = versionDetailsParser;
        _thirdPartyNoticesGenerator = thirdPartyNoticesGenerator;
        _componentListGenerator = componentListGenerator;
        _codeownersGenerator = codeownersGenerator;
        _localGitClient = localGitClient;
        _dependencyFileManager = dependencyFileManager;
        _fileSystem = fileSystem;

        LocalVmr = localGitRepoFactory.Create(_vmrInfo.VmrPath);
    }

    public async Task<IReadOnlyCollection<VmrIngestionPatch>> UpdateRepoToRevisionAsync(
        VmrDependencyUpdate update,
        ILocalGitRepo repoClone,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        string fromRevision,
        (string Name, string Email)? author,
        string commitMessage,
        bool reapplyVmrPatches,
        string? componentTemplatePath,
        string? tpnTemplatePath,
        bool generateCodeowners,
        bool discardPatches,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<VmrIngestionPatch> patches = await _patchHandler.CreatePatches(
            update.Mapping,
            repoClone,
            fromRevision,
            update.TargetRevision,
            _vmrInfo.TmpPath,
            _vmrInfo.TmpPath,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // Get a list of patches that need to be reverted for this update so that repo changes can be applied
        // This includes all patches that are also modified by the current change
        // (happens when we update repo from which the VMR patches come)
        var vmrPatchesToRestore = await RestoreVmrPatchedFilesAsync(update.Mapping, patches, additionalRemotes, cancellationToken);

        foreach (var patch in patches)
        {
            await _patchHandler.ApplyPatch(patch, _vmrInfo.VmrPath, discardPatches, reverseApply: false, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }

        _dependencyInfo.UpdateDependencyVersion(update);

        if (componentTemplatePath != null)
        {
            await _componentListGenerator.UpdateComponentList(componentTemplatePath);
        }

        var filesToAdd = new List<string>
        {
            VmrInfo.GitInfoSourcesDir,
            _vmrInfo.SourceManifestPath
        };

        if (_fileSystem.FileExists(_vmrInfo.VmrPath / VmrInfo.ComponentListPath))
        {
            filesToAdd.Add(VmrInfo.ComponentListPath);
        }

        await _localGitClient.StageAsync(_vmrInfo.VmrPath, filesToAdd, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        if (reapplyVmrPatches)
        {
            await ReapplyVmrPatchesAsync(vmrPatchesToRestore.DistinctBy(p => p.Path).ToArray(), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (tpnTemplatePath != null)
        {
            await UpdateThirdPartyNoticesAsync(tpnTemplatePath, cancellationToken);
        }

        if (generateCodeowners)
        {
            await _codeownersGenerator.UpdateCodeowners(cancellationToken);
        }

        // Commit without adding files as they were added to index directly
        await CommitAsync(commitMessage, author);

        // TODO: Workaround for cases when we get CRLF problems on Windows
        // We should figure out why restoring and reapplying VMR patches leaves working tree with EOL changes
        // https://github.com/dotnet/arcade-services/issues/3277
        if (reapplyVmrPatches && vmrPatchesToRestore.Any() && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _localGitClient.CheckoutAsync(_vmrInfo.VmrPath, ".");
        }

        return vmrPatchesToRestore;
    }

    protected async Task ReapplyVmrPatchesAsync(
        IReadOnlyCollection<VmrIngestionPatch> patches,
        CancellationToken cancellationToken)
    {
        if (patches.Count == 0)
        {
            return;
        }

        _logger.LogInformation("Re-applying {count} VMR patch{s}...",
            patches.Count,
            patches.Count > 1 ? "es" : string.Empty);

        foreach (var patch in patches)
        {
            if (!_fileSystem.FileExists(patch.Path))
            {
                // Patch was removed, so it doesn't exist anymore
                _logger.LogDebug("Not re-applying {patch} as it was removed", patch.Path);
                continue;
            }

            await _patchHandler.ApplyPatch(patch, _vmrInfo.VmrPath, false, reverseApply: false, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }

        _logger.LogInformation("VMR patches re-applied back onto the VMR");
    }

    protected async Task CommitAsync(string commitMessage, (string Name, string Email)? author = null)
    {
        _logger.LogInformation("Committing..");

        var watch = Stopwatch.StartNew();

        await _localGitClient.CommitAsync(_vmrInfo.VmrPath, commitMessage, true, author);

        _logger.LogInformation("Committed in {duration} seconds", (int) watch.Elapsed.TotalSeconds);
    }

    /// <summary>
    /// Recursively parses Version.Details.xml files of all repositories and returns the list of source build dependencies.
    /// </summary>
    protected async Task<IEnumerable<VmrDependencyUpdate>> GetAllDependenciesAsync(
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
                if (!_dependencyInfo.TryGetMapping(dependency.SourceBuild.RepoName, out var mapping))
                {
                    throw new InvalidOperationException(
                        $"No source mapping named '{dependency.SourceBuild.RepoName}' found " +
                        $"for a {VersionFiles.VersionDetailsXml} dependency of {dependency.Name}");
                }

                var update = new VmrDependencyUpdate(
                    mapping,
                    dependency.RepoUri,
                    dependency.Commit,
                    dependency.Version,
                    repo.Mapping);

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
            var path = _vmrInfo.VmrPath / VmrInfo.RelativeSourcesDir / localVersion.Path / VersionFiles.VersionDetailsXml;
            var content = await _fileSystem.ReadAllTextAsync(path);
            return _versionDetailsParser.ParseVersionDetailsXml(content, includePinned: true).Dependencies;
        }

        VersionDetails versionDetails = await _dependencyFileManager.ParseVersionDetailsXmlAsync(remoteRepoUri, commitSha, includePinned: true);
        return versionDetails.Dependencies;
    }

    protected async Task UpdateThirdPartyNoticesAsync(string templatePath, CancellationToken cancellationToken)
    {
        var isTpnUpdated = (await _localGitClient
            .GetStagedFiles(_vmrInfo.VmrPath))
            .Where(ThirdPartyNoticesGenerator.IsTpnPath)
            .Any();

        if (isTpnUpdated)
        {
            await _thirdPartyNoticesGenerator.UpdateThirdPartyNotices(templatePath);
            await _localGitClient.StageAsync(_vmrInfo.VmrPath, new[] { VmrInfo.ThirdPartyNoticesFileName }, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    protected abstract Task<IReadOnlyCollection<VmrIngestionPatch>> RestoreVmrPatchedFilesAsync(
        SourceMapping mapping,
        IReadOnlyCollection<VmrIngestionPatch> patches,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        CancellationToken cancellationToken);

    /// <summary>
    /// Takes a given commit message template and populates it with given values, URLs and others.
    /// </summary>
    /// <param name="template">Template into which the values are filled into</param>
    /// <param name="mapping">Repository mapping</param>
    /// <param name="oldSha">SHA we are updating from</param>
    /// <param name="newSha">SHA we are updating to</param>
    /// <param name="additionalMessage">Additional message inserted in the commit body</param>
    protected static string PrepareCommitMessage(
        string template,
        string name,
        string remote,
        string? oldSha = null,
        string? newSha = null,
        string? additionalMessage = null)
    {
        var replaces = new Dictionary<string, string?>
        {
            { "name", name },
            { "remote", remote },
            { "oldSha", oldSha },
            { "newSha", newSha },
            { "oldShaShort", oldSha is null ? string.Empty : Commit.GetShortSha(oldSha) },
            { "newShaShort", newSha is null ? string.Empty : Commit.GetShortSha(newSha) },
            { "commitMessage", additionalMessage ?? string.Empty },
        };

        foreach (var replace in replaces)
        {
            template = template.Replace($"{{{replace.Key}}}", replace.Value);
        }

        return template;
    }
}
