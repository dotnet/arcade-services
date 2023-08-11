// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LibGit2Sharp;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public abstract class VmrManagerBase
{
    // String used to mark the commit as automated
    protected const string AUTOMATION_COMMIT_TAG = "[[ commit created by automation ]]";
    protected const string HEAD = "HEAD";
    protected const string InterruptedSyncExceptionMessage = 
        "A new branch was created for the sync and didn't get merged as the sync " +
        "was interrupted. A new sync should start from {original} branch.";

    private readonly IVmrInfo _vmrInfo;
    private readonly ISourceManifest _sourceManifest;
    private readonly IVmrDependencyTracker _dependencyInfo;
    private readonly IVmrPatchHandler _patchHandler;
    private readonly IVersionDetailsParser _versionDetailsParser;
    private readonly IThirdPartyNoticesGenerator _thirdPartyNoticesGenerator;
    private readonly IReadmeComponentListGenerator _readmeComponentListGenerator;
    private readonly ILocalGitRepo _localGitClient;
    private readonly IGitFileManagerFactory _gitFileManagerFactory;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger _logger;

    protected VmrManagerBase(
        IVmrInfo vmrInfo,
        ISourceManifest sourceManifest,
        IVmrDependencyTracker dependencyInfo,
        IVmrPatchHandler vmrPatchHandler,
        IVersionDetailsParser versionDetailsParser,
        IThirdPartyNoticesGenerator thirdPartyNoticesGenerator,
        IReadmeComponentListGenerator readmeComponentListGenerator,
        ILocalGitRepo localGitClient,
        IGitFileManagerFactory gitFileManagerFactory,
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
        _readmeComponentListGenerator = readmeComponentListGenerator;
        _localGitClient = localGitClient;
        _gitFileManagerFactory = gitFileManagerFactory;
        _fileSystem = fileSystem;
    }

    protected async Task<IReadOnlyCollection<VmrIngestionPatch>> UpdateRepoToRevision(
        VmrDependencyUpdate update,
        LocalPath clonePath,
        string fromRevision,
        Signature author,
        string commitMessage,
        bool reapplyVmrPatches,
        string? readmeTemplatePath,
        string? tpnTemplatePath,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<VmrIngestionPatch> patches = await _patchHandler.CreatePatches(
            update.Mapping,
            clonePath,
            fromRevision,
            update.TargetRevision,
            _vmrInfo.TmpPath,
            _vmrInfo.TmpPath,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // Get a list of patches that need to be reverted for this update so that repo changes can be applied
        // This includes all patches that are also modified by the current change
        // (happens when we update repo from which the VMR patches come)
        var vmrPatchesToRestore = await RestoreVmrPatchedFiles(update.Mapping, patches, cancellationToken);

        foreach (var patch in patches)
        {
            await _patchHandler.ApplyPatch(patch, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }

        _dependencyInfo.UpdateDependencyVersion(update);

        if (readmeTemplatePath != null)
        {
            await _readmeComponentListGenerator.UpdateReadme(readmeTemplatePath);
        }
        
        Commands.Stage(new Repository(_vmrInfo.VmrPath), new string[]
        {
            VmrInfo.ReadmeFileName,
            VmrInfo.GitInfoSourcesDir,
            _vmrInfo.GetSourceManifestPath()
        });

        cancellationToken.ThrowIfCancellationRequested();

        if (reapplyVmrPatches)
        {
            await ReapplyVmrPatches(vmrPatchesToRestore.DistinctBy(p => p.Path).ToArray(), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (tpnTemplatePath != null)
        {
            await UpdateThirdPartyNotices(tpnTemplatePath, cancellationToken);
        }

        // Commit without adding files as they were added to index directly
        Commit(commitMessage, author);

        return vmrPatchesToRestore;
    }

    protected async Task ReapplyVmrPatches(
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

            await _patchHandler.ApplyPatch(patch, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }

        _logger.LogInformation("VMR patches re-applied back onto the VMR");
    }

    protected void Commit(string commitMessage, Signature author)
    {
        _logger.LogInformation("Committing..");

        var watch = Stopwatch.StartNew();
        using var repository = new Repository(_vmrInfo.VmrPath);
        var options = new CommitOptions { AllowEmptyCommit = true };
        var commit = repository.Commit(commitMessage, author, DotnetBotCommitSignature, options);

        _logger.LogInformation("Created {sha} in {duration} seconds", DarcLib.Commit.GetShortSha(commit.Id.Sha), (int) watch.Elapsed.TotalSeconds);
    }

    /// <summary>
    /// Recursively parses Version.Details.xml files of all repositories and returns the list of source build dependencies.
    /// </summary>
    protected async Task<IEnumerable<VmrDependencyUpdate>> GetAllDependencies(
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
                .Prepend(repo.RemoteUri)
                .ToArray();

            IEnumerable<DependencyDetail>? repoDependencies = null;
            foreach (var remoteUri in remotes)
            {
                try
                {
                    repoDependencies = (await GetRepoDependencies(remoteUri, repo.TargetRevision))
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
                var mapping = _dependencyInfo.Mappings.FirstOrDefault(m => m.Name == dependency.SourceBuild.RepoName)
                    ?? throw new InvalidOperationException(
                        $"No source mapping named '{dependency.SourceBuild.RepoName}' found " +
                        $"for a {VersionFiles.VersionDetailsXml} dependency of {dependency.Name}");

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

    private async Task<IEnumerable<DependencyDetail>> GetRepoDependencies(string remoteRepoUri, string commitSha)
    {
        // Check if we have the file locally
        var localVersion = _sourceManifest.Repositories.FirstOrDefault(repo => repo.RemoteUri == remoteRepoUri);
        if (localVersion?.CommitSha == commitSha)
        {
            var path = _vmrInfo.VmrPath / VmrInfo.RelativeSourcesDir / localVersion.Path / VersionFiles.VersionDetailsXml;
            var content = await _fileSystem.ReadAllTextAsync(path);
            return _versionDetailsParser.ParseVersionDetailsXml(content, includePinned: true);
        }

        var gitFileManager = _gitFileManagerFactory.Create(remoteRepoUri);

        return await gitFileManager.ParseVersionDetailsXmlAsync(remoteRepoUri, commitSha, includePinned: true);
    }

    protected async Task UpdateThirdPartyNotices(string templatePath, CancellationToken cancellationToken)
    {
        var isTpnUpdated = _localGitClient
            .GetStagedFiles(_vmrInfo.VmrPath)
            .Where(ThirdPartyNoticesGenerator.IsTpnPath)
            .Any();

        if (isTpnUpdated)
        {
            await _thirdPartyNoticesGenerator.UpdateThirdPartyNotices(templatePath);
            await _localGitClient.Stage(_vmrInfo.VmrPath, new[] { VmrInfo.ThirdPartyNoticesFileName });
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    protected abstract Task<IReadOnlyCollection<VmrIngestionPatch>> RestoreVmrPatchedFiles(
        SourceMapping mapping,
        IReadOnlyCollection<VmrIngestionPatch> patches,
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
            { "oldShaShort", oldSha is null ? string.Empty : DarcLib.Commit.GetShortSha(oldSha) },
            { "newShaShort", newSha is null ? string.Empty : DarcLib.Commit.GetShortSha(newSha) },
            { "commitMessage", additionalMessage ?? string.Empty },
        };

        foreach (var replace in replaces)
        {
            template = template.Replace($"{{{replace.Key}}}", replace.Value);
        }

        return template;
    }

    protected static string GetShaForRef(string repoPath, string? gitRef)
    {
        if (gitRef == Constants.EmptyGitObject)
        {
            return gitRef;
        }

        using var repository = new Repository(repoPath);
        var commit = gitRef is null
            ? repository.Commits.FirstOrDefault()
            : repository.Lookup<LibGit2Sharp.Commit>(gitRef);

        return commit?.Id.Sha ?? throw new InvalidOperationException($"Failed to find commit {gitRef} in {repository.Info.Path}");
    }

    protected static Signature DotnetBotCommitSignature => new(Constants.DarcBotName, Constants.DarcBotEmail, DateTimeOffset.Now);

    /// <summary>
    /// Helper method that creates a new git branch that we can make changes to.
    /// After we're done, the branch can be merged into the original branch.
    /// </summary>
    protected IWorkBranch CreateWorkBranch(string branchName) => WorkBranch.CreateWorkBranch(_vmrInfo.VmrPath, branchName, _logger);

    protected interface IWorkBranch
    {
        void MergeBack(string commitMessage);
        string OriginalBranch { get; }
    }
    
    /// <summary>
    /// Helper class that creates a new git branch when initialized and can merge this branch back into the original branch.
    /// </summary>
    private class WorkBranch : IWorkBranch
    {
        private readonly string _repoPath;
        private readonly string _currentBranch;
        private readonly string _workBranch;
        private readonly ILogger _logger;

        public string OriginalBranch => _currentBranch;

        private WorkBranch(string repoPath, string currentBranch, string workBranch, ILogger logger)
        {
            _repoPath = repoPath;
            _currentBranch = currentBranch;
            _workBranch = workBranch;
            _logger = logger;
        }

        public static WorkBranch CreateWorkBranch(string repoPath, string branchName, ILogger logger)
        {
            string originalBranch;

            using (var repo = new Repository(repoPath))
            {
                originalBranch = repo.Head.FriendlyName;

                if (originalBranch == branchName)
                {
                    var message = $"You are already on branch {branchName}. " +
                                    "Previous sync probably failed and left the branch unmerged. " +
                                    "To complete the sync checkout the original branch and try again.";

                    throw new Exception(message);
                }

                logger.LogInformation("Creating a temporary work branch {branchName}", branchName);
                
                Branch branch = repo.Branches.Add(branchName, HEAD, allowOverwrite: true);
                Commands.Checkout(repo, branch);
            }

            return new WorkBranch(repoPath, originalBranch, branchName, logger);
        }

        public void MergeBack(string commitMessage)
        {
            using var repo = new Repository(_repoPath);
            _logger.LogInformation("Merging {branchName} into {mainBranch}", _workBranch, _currentBranch);
            Commands.Checkout(repo, _currentBranch);
            repo.Merge(repo.Branches[_workBranch], DotnetBotCommitSignature, new MergeOptions
            {
                FastForwardStrategy = FastForwardStrategy.NoFastForward,
                CommitOnSuccess = false,
            });

            repo.Commit(commitMessage, DotnetBotCommitSignature, DotnetBotCommitSignature);
        }
    }
}
