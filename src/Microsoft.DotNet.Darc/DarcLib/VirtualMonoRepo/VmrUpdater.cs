// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

/// <summary>
/// This class is able to update an individual repository within the VMR from one commit to another.
/// It creates git diffs while adhering to cloaking rules, accommodating for patched files, resolving submodules.
/// It can also update other repositories recursively based on the dependencies stored in Version.Details.xml.
/// </summary>
public class VmrUpdater : VmrManagerBase, IVmrUpdater
{
    // Message used when synchronizing multiple commits as one
    private const string SquashCommitMessage =
        $$"""
        [{name}] Sync {oldShaShort}{{Constants.Arrow}}{newShaShort}
        Diff: {remote}/compare/{oldSha}..{newSha}
        
        From: {remote}/commit/{oldSha}
        To: {remote}/commit/{newSha}
        
        {{Constants.AUTOMATION_COMMIT_TAG}}
        """;

    // Message used when finalizing the sync with a merge commit
    private const string MergeCommitMessage =
        $$"""
        [Recursive sync] {name} / {oldShaShort}{{Constants.Arrow}}{newShaShort}
        
        Updated repositories:
        {commitMessage}
        
        {{Constants.AUTOMATION_COMMIT_TAG}}
        """;

    private readonly IVmrInfo _vmrInfo;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly IRepositoryCloneManager _cloneManager;
    private readonly IVmrPatchHandler _patchHandler;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<VmrUpdater> _logger;
    private readonly ISourceManifest _sourceManifest;
    private readonly IThirdPartyNoticesGenerator _thirdPartyNoticesGenerator;
    private readonly IReadmeComponentListGenerator _readmeComponentListGenerator;
    private readonly ILocalGitClient _localGitClient;
    private readonly IWorkBranchFactory _workBranchFactory;

    public VmrUpdater(
        IVmrDependencyTracker dependencyTracker,
        IVersionDetailsParser versionDetailsParser,
        IRepositoryCloneManager cloneManager,
        IVmrPatchHandler patchHandler,
        IThirdPartyNoticesGenerator thirdPartyNoticesGenerator,
        IReadmeComponentListGenerator readmeComponentListGenerator,
        ICodeownersGenerator codeownersGenerator,
        ILocalGitClient localGitClient,
        IDependencyFileManager dependencyFileManager,
        IWorkBranchFactory workBranchFactory,
        IFileSystem fileSystem,
        ILogger<VmrUpdater> logger,
        ISourceManifest sourceManifest,
        IVmrInfo vmrInfo)
        : base(vmrInfo, sourceManifest, dependencyTracker, patchHandler, versionDetailsParser, thirdPartyNoticesGenerator, readmeComponentListGenerator, codeownersGenerator, localGitClient, dependencyFileManager, fileSystem, logger)
    {
        _logger = logger;
        _sourceManifest = sourceManifest;
        _vmrInfo = vmrInfo;
        _dependencyTracker = dependencyTracker;
        _cloneManager = cloneManager;
        _patchHandler = patchHandler;
        _fileSystem = fileSystem;
        _thirdPartyNoticesGenerator = thirdPartyNoticesGenerator;
        _readmeComponentListGenerator = readmeComponentListGenerator;
        _localGitClient = localGitClient;
        _workBranchFactory = workBranchFactory;
    }

    public async Task UpdateRepository(
        string mappingName,
        string? targetRevision,
        string? targetVersion,
        bool updateDependencies,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        string? readmeTemplatePath,
        string? tpnTemplatePath,
        bool generateCodeowners,
        bool discardPatches,
        CancellationToken cancellationToken)
    {
        await _dependencyTracker.InitializeSourceMappings();

        var mapping = _dependencyTracker.Mappings
            .FirstOrDefault(m => m.Name.Equals(mappingName, StringComparison.InvariantCultureIgnoreCase))
            ?? throw new Exception($"No mapping named '{mappingName}' found");

        if (_vmrInfo.SourceMappingsPath != null
            && _vmrInfo.SourceMappingsPath.StartsWith(VmrInfo.GetRelativeRepoSourcesPath(mapping)))
        {
            var fileRelativePath = _vmrInfo.SourceMappingsPath.Substring(VmrInfo.GetRelativeRepoSourcesPath(mapping).Length);

            var remotes = additionalRemotes
                .Where(r => r.Mapping == mappingName)
                .Select(r => r.RemoteUri)
                .Prepend(mapping.DefaultRemote)
                .ToArray();

            var clonePath = await _cloneManager.PrepareClone(
                mapping,
                remotes,
                targetRevision ?? mapping.DefaultRef, 
                cancellationToken);

            _logger.LogDebug($"Loading a new version of source mappings from {clonePath / fileRelativePath}");
            await _dependencyTracker.InitializeSourceMappings(clonePath / fileRelativePath);
            
            mapping = _dependencyTracker.Mappings
                .FirstOrDefault(m => m.Name.Equals(mappingName, StringComparison.InvariantCultureIgnoreCase))
                ?? throw new Exception($"No mapping named '{mappingName}' found");
        }

        var dependencyUpdate = new VmrDependencyUpdate(
            mapping,
            mapping.DefaultRemote,
            targetRevision ?? mapping.DefaultRef,
            targetVersion,
            Parent: null);

        if (updateDependencies)
        {
            await UpdateRepositoryRecursively(
                dependencyUpdate,
                additionalRemotes,
                readmeTemplatePath,
                tpnTemplatePath,
                generateCodeowners,
                discardPatches,
                cancellationToken);
        }
        else
        {
            await UpdateRepositoryInternal(
                dependencyUpdate,
                reapplyVmrPatches: true,
                additionalRemotes,
                readmeTemplatePath,
                tpnTemplatePath,
                generateCodeowners,
                discardPatches,
                cancellationToken);
        }
    }

    private async Task<IReadOnlyCollection<VmrIngestionPatch>> UpdateRepositoryInternal(
        VmrDependencyUpdate update,
        bool reapplyVmrPatches,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        string? readmeTemplatePath,
        string? tpnTemplatePath,
        bool generateCodeowners,
        bool discardPatches,
        CancellationToken cancellationToken)
    {
        VmrDependencyVersion currentVersion = _dependencyTracker.GetDependencyVersion(update.Mapping)
            ?? throw new Exception($"Failed to find current version for {update.Mapping.Name}");

        _logger.LogInformation("Synchronizing {name} from {current} to {repo} / {revision}",
            update.Mapping.Name,
            currentVersion.Sha,
            update.RemoteUri,
            update.TargetRevision);

        // Do we need to change anything?
        if (currentVersion.Sha == update.TargetRevision)
        {
            if (currentVersion.PackageVersion != update.TargetVersion && update.TargetVersion != null)
            {
                await UpdateTargetVersionOnly(
                    update.TargetRevision,
                    update.TargetVersion,
                    update.Mapping,
                    currentVersion,
                    cancellationToken);
            }
            else
            {
                _logger.LogInformation("Repository {repo} is already at {sha}", update.Mapping.Name, update.TargetRevision);
            }

            return Array.Empty<VmrIngestionPatch>();
        }

        var remotes = additionalRemotes
            .Where(r => r.Mapping == update.Mapping.Name)
            .Select(r => r.RemoteUri)
            // Add remotes for where we synced last from and where we are syncing to (e.g. github.com -> dev.azure.com)
            .Prepend(_sourceManifest.Repositories.First(r => r.Path == update.Mapping.Name).RemoteUri)
            .Prepend(update.RemoteUri)
            .ToArray();

        NativePath clonePath = await _cloneManager.PrepareClone(
            update.Mapping,
            remotes,
            update.TargetRevision,
            cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        update = update with
        {
            TargetRevision = await _localGitClient.GetShaForRefAsync(clonePath, update.TargetRevision)
        };

        if (currentVersion.Sha == update.TargetRevision)
        {
            _logger.LogInformation("No new commits found to synchronize");
            return Array.Empty<VmrIngestionPatch>();
        }

        _logger.LogInformation("Updating {repo} from {current} to {next}..",
            update.Mapping.Name, Commit.GetShortSha(currentVersion.Sha), Commit.GetShortSha(update.TargetRevision));

        var commitMessage = PrepareCommitMessage(
            SquashCommitMessage,
            update.Mapping.Name,
            update.RemoteUri,
            currentVersion.Sha,
            update.TargetRevision);

        return await UpdateRepoToRevisionAsync(
            update,
            clonePath,
            currentVersion.Sha,
            author: null,
            commitMessage,
            reapplyVmrPatches,
            readmeTemplatePath,
            tpnTemplatePath,
            generateCodeowners,
            discardPatches,
            cancellationToken);
    }

    /// <summary>
    /// Updates a repository and all of it's dependencies recursively starting with a given mapping.
    /// Always updates to the first version found per repository in the dependency tree.
    /// </summary>
    private async Task UpdateRepositoryRecursively(
        VmrDependencyUpdate rootUpdate,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        string? readmeTemplatePath,
        string? tpnTemplatePath,
        bool generateCodeowners,
        bool discardPatches,
        CancellationToken cancellationToken)
    {
        string originalRootSha = GetCurrentVersion(rootUpdate.Mapping);
        _logger.LogInformation("Recursive update for {repo} / {from}{arrow}{to}",
            rootUpdate.Mapping.Name,
            Commit.GetShortSha(originalRootSha),
            Constants.Arrow,
            rootUpdate.TargetRevision);

        var updates = (await GetAllDependenciesAsync(rootUpdate, additionalRemotes, cancellationToken)).ToList();

        var extraneousMappings = _dependencyTracker.Mappings
            .Where(mapping => !updates.Any(update => update.Mapping == mapping))
            .Select(mapping => mapping.Name);

        if (extraneousMappings.Any())
        {
            var separator = $"{Environment.NewLine}  - ";
            _logger.LogWarning($"The following mappings do not appear in current update's dependency tree:{separator}{{extraMappings}}",
                string.Join(separator, extraneousMappings));
        }

        // When we synchronize in bulk, we do it in a separate branch that we then merge into the main one

        var workBranchName = "sync" +
            $"/{rootUpdate.Mapping.Name}" +
            $"/{Commit.GetShortSha(GetCurrentVersion(rootUpdate.Mapping))}-{rootUpdate.TargetRevision}";
        IWorkBranch workBranch = await _workBranchFactory.CreateWorkBranchAsync(_vmrInfo.VmrPath, workBranchName);

        // Collection of all affected VMR patches we will need to restore after the sync
        var vmrPatchesToReapply = new List<VmrIngestionPatch>();

        // Dependencies that were already updated during this run
        var updatedDependencies = new HashSet<VmrDependencyUpdate>();

        foreach (VmrDependencyUpdate update in updates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string currentSha;
            try
            {
                currentSha = GetCurrentVersion(update.Mapping);
            }
            catch (RepositoryNotInitializedException)
            {
                _logger.LogWarning("Dependency {repo} has not been initialized in the VMR yet, repository will be initialized",
                    update.Mapping.Name);

                currentSha = Constants.EmptyGitObject;
                _dependencyTracker.UpdateDependencyVersion(update with
                {
                    TargetRevision = currentSha
                });
            }

            if (update.Parent is not null)
            {
                _logger.LogInformation("Recursively updating {parent}'s dependency {repo} / {from}{arrow}{to}",
                    update.Parent.Name,
                    update.Mapping.Name,
                    currentSha,
                    Constants.Arrow,
                    update.TargetRevision);
            }

            IReadOnlyCollection<VmrIngestionPatch> patchesToReapply;
            try
            {
                patchesToReapply = await UpdateRepositoryInternal(
                    update,
                    false,
                    additionalRemotes,
                    readmeTemplatePath,
                    tpnTemplatePath,
                    generateCodeowners,
                    discardPatches,
                    cancellationToken);
            }
            catch(Exception)
            {
                _logger.LogWarning(
                    InterruptedSyncExceptionMessage,
                    workBranch.OriginalBranch.StartsWith("sync") || workBranch.OriginalBranch.StartsWith("init")
                        ? "the original"
                        : workBranch.OriginalBranch);
                throw;
            }

            vmrPatchesToReapply.AddRange(patchesToReapply);

            updatedDependencies.Add(update with
            {
                // We resolve the SHA again because original target could have been a branch name
                TargetRevision = GetCurrentVersion(update.Mapping),

                // We also store the original SHA (into the version!) so that later we use it in the commit message
                TargetVersion = currentSha,
            });
        }

        string finalRootSha = GetCurrentVersion(rootUpdate.Mapping);
        var summaryMessage = new StringBuilder();

        foreach (var update in updatedDependencies)
        {
            if (update.TargetRevision == update.TargetVersion)
            {
                continue;
            }

            var fromShort = Commit.GetShortSha(update.TargetVersion);
            var toShort = Commit.GetShortSha(update.TargetRevision);
            summaryMessage
                .AppendLine($"  - {update.Mapping.Name} / {fromShort}{Constants.Arrow}{toShort}")
                .AppendLine($"    {update.RemoteUri}/compare/{update.TargetVersion}..{update.TargetRevision}");
        }

        if (vmrPatchesToReapply.Any())
        {
            try
            {
                await ReapplyVmrPatchesAsync(vmrPatchesToReapply.DistinctBy(p => p.Path).ToArray(), cancellationToken);
            }
            catch (Exception)
            {
                _logger.LogWarning(
                    InterruptedSyncExceptionMessage,
                    workBranch.OriginalBranch.StartsWith("sync") || workBranch.OriginalBranch.StartsWith("init")
                        ? "the original"
                        : workBranch.OriginalBranch);
                throw;
            }

            await CommitAsync("[VMR patches] Re-apply VMR patches");
        }

        await CleanUpRemovedRepos(readmeTemplatePath, tpnTemplatePath);

        var commitMessage = PrepareCommitMessage(
            MergeCommitMessage,
            rootUpdate.Mapping.Name,
            rootUpdate.Mapping.DefaultRemote,
            originalRootSha,
            finalRootSha,
            summaryMessage.ToString());
        
        await workBranch.MergeBackAsync(commitMessage);

        _logger.LogInformation("Recursive update for {repo} finished.{newLine}{message}",
            rootUpdate.Mapping.Name,
            Environment.NewLine,
            summaryMessage);
    }

    /// <summary>
    /// Detects VMR patches affected by a given set of patches and restores files patched by these
    /// VMR patches into their original state.
    /// Detects whether patched files are coming from a mapped repository or a submodule too.
    /// </summary>
    /// <param name="updatedMapping">Mapping that is currently being updated (so we get its patches)</param>
    /// <param name="patches">Patches with incoming changes to be checked whether they affect some VMR patch</param>
    protected override async Task<IReadOnlyCollection<VmrIngestionPatch>> RestoreVmrPatchedFilesAsync(
        SourceMapping updatedMapping,
        IReadOnlyCollection<VmrIngestionPatch> patches,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<VmrIngestionPatch> vmrPatchesToRestore = await GetVmrPatchesToRestore(
            updatedMapping,
            patches,
            cancellationToken);

        if (vmrPatchesToRestore.Count == 0)
        {
            return vmrPatchesToRestore;
        }

        // We order the possible sources by length so that when a patch applies onto a submodule we will
        // detect the most nested one
        List<ISourceComponent> sources = _sourceManifest.Submodules
            .Concat(_sourceManifest.Repositories)
            .OrderByDescending(s => s.Path.Length)
            .ToList();

        ISourceComponent FindComponentForFile(string file)
        {
            return sources.FirstOrDefault(component => file.StartsWith("src/" + component.Path))
                ?? throw new Exception($"Failed to find mapping/submodule for file '{file}'");
        }

        _logger.LogInformation("Found {count} VMR patches to restore. Getting affected files...",
            vmrPatchesToRestore.Count);

        // First we collect all files that are affected + their origin (repo or submodule)
        var affectedFiles = new List<(UnixPath RepoPath, UnixPath VmrPath, ISourceComponent Origin)>();
        foreach (VmrIngestionPatch patch in vmrPatchesToRestore)
        {
            if (!_fileSystem.FileExists(patch.Path))
            {
                // Patch is being added, so it doesn't exist yet
                _logger.LogDebug("Not restoring {patch} as it will be added during the sync", patch.Path);
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogDebug("Detecting patched files from a VMR patch `{patch}`..", patch.Path);

            IReadOnlyCollection<UnixPath> patchedFiles = await _patchHandler.GetPatchedFiles(patch.Path, cancellationToken);

            foreach (UnixPath patchedFile in patchedFiles)
            {
                UnixPath vmrPath = patch.ApplicationPath != null ? patch.ApplicationPath / patchedFile : new UnixPath("");
                ISourceComponent parentComponent = FindComponentForFile(vmrPath);
                UnixPath repoPath = new(vmrPath.Path.Replace(VmrInfo.SourcesDir / parentComponent.Path + '/', null));

                affectedFiles.Add((repoPath, vmrPath, parentComponent));
            }

            _logger.LogDebug("{count} files restored from a VMR patch `{patch}`..", patchedFiles.Count, patch.Path);
        }

        _logger.LogInformation("Found {count} files affected by VMR patches. Restoring original files...",
            affectedFiles.Count);

        // We will group files by where they come from (remote URI + SHA) so that we do as few clones as possible
        var groups = affectedFiles.GroupBy(x => x.Origin, x => (x.RepoPath, x.VmrPath));
        foreach (var group in groups)
        {
            var source = group.Key;
            _logger.LogDebug("Restoring {count} patched files from {uri} / {sha}...",
                group.Count(),
                source.RemoteUri,
                source.CommitSha);

            var clonePath = await _cloneManager.PrepareClone(source.RemoteUri, source.CommitSha, cancellationToken);

            foreach ((UnixPath repoPath, UnixPath pathInVmr) in group)
            {
                cancellationToken.ThrowIfCancellationRequested();

                LocalPath originalFile = clonePath / repoPath;
                LocalPath destination = _vmrInfo.VmrPath / pathInVmr;

                if (_fileSystem.FileExists(originalFile))
                {
                    // Copy old revision to VMR
                    _logger.LogDebug("Restoring file `{destination}` from original at `{originalFile}`..", destination, originalFile);
                    _fileSystem.CopyFile(originalFile, destination, overwrite: true);
                    await _localGitClient.StageAsync(_vmrInfo.VmrPath, new string[] { pathInVmr }, cancellationToken);
                }
                else if (_fileSystem.FileExists(destination))
                {
                    // File is being added by the patch - we need to remove it
                    _logger.LogDebug("Removing file `{destination}` which is added by a patch..", destination);
                    _fileSystem.DeleteFile(destination);
                    await _localGitClient.StageAsync(_vmrInfo.VmrPath, new string[] { pathInVmr }, cancellationToken);
                }
                // else file is being added together with a patch at the same time
            }
        }

        _logger.LogInformation("Files affected by VMR patches restored");

        return vmrPatchesToRestore;
    }

    /// <summary>
    /// Gets a list of VMR patches that need to be reverted for a given mapping update so that repo changes can be applied.
    /// Usually, this just returns all VMR patches for that given mapping (e.g. for the aspnetcore returns all aspnetcore only patches).
    /// 
    /// One exception is when the updated mapping is the one that the VMR patches come from into the VMR (e.g. dotnet/installer).
    /// In this case, we also check which VMR patches are modified by the change and we also returns those.
    /// Examples:
    ///   - An aspnetcore VMR patch is removed from installer - we must remove it from the files it is applied to in the VMR.
    ///   - A new version of patch is synchronized from installer - we must remove the old version and apply the new.
    /// </summary>
    /// <param name="updatedMapping">Currently synchronized mapping</param>
    /// <param name="patches">Patches of currently synchronized changes</param>
    private async Task<IReadOnlyCollection<VmrIngestionPatch>> GetVmrPatchesToRestore(
        SourceMapping updatedMapping,
        IReadOnlyCollection<VmrIngestionPatch> patches,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting a list of VMR patches to restore for {repo} before we ingest new changes...", updatedMapping.Name);

        var patchesToRestore = new List<VmrIngestionPatch>();

        // Always restore all patches belonging to the currently updated mapping
        foreach (var vmrPatch in _patchHandler.GetVmrPatches(updatedMapping))
        {
            patchesToRestore.Add(new VmrIngestionPatch(vmrPatch, updatedMapping));
        }

        // If we are not updating the mapping that the VMR patches come from, we're done
        if (_vmrInfo.PatchesPath == null || !_vmrInfo.PatchesPath.StartsWith(VmrInfo.GetRelativeRepoSourcesPath(updatedMapping)))
        {
            return patchesToRestore;
        }

        _logger.LogInformation("Repo {repo} contains VMR patches, checking which VMR patches have changes...", updatedMapping.Name);

        // Check which files are modified by every of the patches that bring new changes into the VMR
        foreach (var patch in patches)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyCollection<UnixPath> patchedFiles = await _patchHandler.GetPatchedFiles(patch.Path, cancellationToken);
            IEnumerable<LocalPath> affectedPatches = patchedFiles
                .Select(path => VmrInfo.GetRelativeRepoSourcesPath(updatedMapping) / path)
                .Where(path => path.Path.StartsWith(_vmrInfo.PatchesPath) && path.Path.EndsWith(".patch"))
                .Select(path => _vmrInfo.VmrPath / path);

            foreach (LocalPath affectedPatch in affectedPatches)
            {
                // patch is in the folder named as the mapping for which it is applied
                var affectedRepo = affectedPatch.Path.Split(_fileSystem.DirectorySeparatorChar)[^2];
                var affectedMapping = _dependencyTracker.Mappings.First(m => m.Name == affectedRepo);

                _logger.LogInformation("Detected a change of a VMR patch {patch} for {repo}", affectedPatch, affectedRepo);
                patchesToRestore.Add(new VmrIngestionPatch(affectedPatch, affectedMapping));
            }
        }

        return patchesToRestore;
    }

    private string GetCurrentVersion(SourceMapping mapping)
    {
        var version = _dependencyTracker.GetDependencyVersion(mapping);

        if (version is null)
        {
            throw new RepositoryNotInitializedException($"Repository {mapping.Name} has not been initialized yet");
        }

        return version.Sha;
    }

    private async Task CleanUpRemovedRepos(string? readmeTemplatePath, string? tpnTemplatePath)
    {
        var deletedRepos = _sourceManifest
            .Repositories
            .Where(r => _dependencyTracker.Mappings.FirstOrDefault(m => m.Name == r.Path) == null)
            .ToList();

        if (!deletedRepos.Any())
        {
            return;
        }

        foreach (var repo in deletedRepos)
        {
            _logger.LogWarning("The mapping for {name} was deleted. Removing the repository from the VMR.", repo.Path);
            DeleteRepository(repo.Path);
        }

        var sourceManifestPath = _vmrInfo.GetSourceManifestPath();
        _fileSystem.WriteToFile(sourceManifestPath, _sourceManifest.ToJson());

        if (readmeTemplatePath != null)
        {
            await _readmeComponentListGenerator.UpdateReadme(readmeTemplatePath);
        }

        if (tpnTemplatePath != null)
        {
            await _thirdPartyNoticesGenerator.UpdateThirdPartyNotices(tpnTemplatePath);
        }

        await _localGitClient.StageAsync(_vmrInfo.VmrPath, new string[] { "*" });
        var commitMessage = "Delete " + string.Join(", ", deletedRepos.Select(r => r.Path));
        await CommitAsync(commitMessage);
    }

    private void DeleteRepository(string repo)
    {
        var repoSourcesDir = _vmrInfo.GetRepoSourcesPath(repo);
        try
        {
            _fileSystem.DeleteDirectory(repoSourcesDir, true);
            _logger.LogInformation("Deleted directory {dir}", repoSourcesDir);
        }
        catch (DirectoryNotFoundException)
        {
            _logger.LogInformation("Directory {dir} is already deleted", repoSourcesDir);
        }

        _sourceManifest.RemoveRepository(repo);
        _logger.LogInformation("Removed record for repository {name} from {file}", repo, _vmrInfo.GetSourceManifestPath());

        if (_dependencyTracker.RemoveRepositoryVersion(repo))
        {
            _logger.LogInformation("Deleted {repo} version information from git-info", repo);
        } 
        else
        {
            _logger.LogInformation("{repo} version information is already deleted", repo);
        }
    }

    /// <summary>
    /// This method is called in cases when a repository is already at the target revision but the package version
    /// differs. This can happen when a new build from the same commit is synchronized to the VMR.
    /// </summary>
    private async Task UpdateTargetVersionOnly(
        string targetRevision,
        string targetVersion,
        SourceMapping mapping,
        VmrDependencyVersion currentVersion,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "Repository {repo} is already at {sha} but differs in package version ({old} vs {new}). Updating metadata...",
            mapping.Name,
            targetRevision,
            currentVersion.PackageVersion,
            targetVersion);

        _dependencyTracker.UpdateDependencyVersion(new VmrDependencyUpdate(
            Mapping: mapping,
            TargetRevision: targetRevision,
            TargetVersion: targetVersion,
            Parent: null,
            RemoteUri: _sourceManifest.Repositories.First(r => r.Path == mapping.Name).RemoteUri));

        var filesToAdd = new List<string>
        {
            VmrInfo.GitInfoSourcesDir,
            _vmrInfo.GetSourceManifestPath()
        };

        cancellationToken.ThrowIfCancellationRequested();
        await _localGitClient.StageAsync(_vmrInfo.VmrPath, filesToAdd, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await CommitAsync($"Updated package version of {mapping.Name} to {targetVersion}", author: null);
    }

    private class RepositoryNotInitializedException : Exception
    {
        public RepositoryNotInitializedException(string message)
            : base(message)
        {
        }
    }
}
