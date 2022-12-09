// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

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

public abstract class VmrManagerBase : IVmrManager
{
    // String used to mark the commit as automated
    protected const string AUTOMATION_COMMIT_TAG = "[[ commit created by automation ]]";
    protected const string HEAD = "HEAD";

    private readonly IVmrInfo _vmrInfo;
    private readonly ISourceManifest _sourceManifest;
    private readonly IVmrDependencyTracker _dependencyInfo;
    private readonly IVersionDetailsParser _versionDetailsParser;
    private readonly IThirdPartyNoticesGenerator _thirdPartyNoticesGenerator;
    private readonly ILocalGitRepo _localGitClient;
    private readonly IGitFileManagerFactory _gitFileManagerFactory;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger _logger;

    public IReadOnlyCollection<SourceMapping> Mappings => _dependencyInfo.Mappings;

    protected VmrManagerBase(
        IVmrInfo vmrInfo,
        ISourceManifest sourceManifest,
        IVmrDependencyTracker dependencyInfo,
        IVersionDetailsParser versionDetailsParser,
        IThirdPartyNoticesGenerator thirdPartyNoticesGenerator,
        ILocalGitRepo localGitClient,
        IGitFileManagerFactory gitFileManagerFactory,
        IFileSystem fileSystem,
        ILogger<VmrUpdater> logger)
    {
        _logger = logger;
        _vmrInfo = vmrInfo;
        _sourceManifest = sourceManifest;
        _dependencyInfo = dependencyInfo;
        _versionDetailsParser = versionDetailsParser;
        _thirdPartyNoticesGenerator = thirdPartyNoticesGenerator;
        _localGitClient = localGitClient;
        _gitFileManagerFactory = gitFileManagerFactory;
        _fileSystem = fileSystem;
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
    protected async Task<IEnumerable<DependencyUpdate>> GetAllDependencies(DependencyUpdate root, CancellationToken cancellationToken)
    {
        var transitiveDependencies = new Dictionary<SourceMapping, DependencyUpdate>
        {
            { root.Mapping, root },
        };

        var reposToScan = new Queue<DependencyUpdate>();
        reposToScan.Enqueue(transitiveDependencies.Values.Single());

        _logger.LogInformation("Finding transitive dependencies for {mapping}:{revision}..", root.Mapping.Name, root.TargetRevision);

        while (reposToScan.TryDequeue(out var repo))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var repoDependencies = (await GetRepoDependencies(repo.RemoteUri, repo.TargetRevision))
                .Where(dep => dep.SourceBuild is not null);

            foreach (var dependency in repoDependencies)
            {
                var mapping = _dependencyInfo.Mappings.FirstOrDefault(m => m.Name == dependency.SourceBuild.RepoName)
                    ?? throw new InvalidOperationException(
                        $"No source mapping named '{dependency.SourceBuild.RepoName}' found " +
                        $"for a {VersionFiles.VersionDetailsXml} dependency of {dependency.Name}");

                var update = new DependencyUpdate(
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

    protected async Task UpdateThirdPartyNotices(CancellationToken cancellationToken)
    {
        var isTpnUpdated = _localGitClient
            .GetStagedFiles(_vmrInfo.VmrPath)
            .Where(ThirdPartyNoticesGenerator.IsTpnPath)
            .Any();

        if (isTpnUpdated)
        {
            await _thirdPartyNoticesGenerator.UpdateThirtPartyNotices();
            _localGitClient.Stage(_vmrInfo.VmrPath, VmrInfo.ThirdPartyNoticesFileName);
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

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
        SourceMapping mapping,
        string? oldSha = null,
        string? newSha = null,
        string? additionalMessage = null)
    {
        var replaces = new Dictionary<string, string?>
        {
            { "name", mapping.Name },
            { "remote", mapping.DefaultRemote },
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
                logger.LogInformation("Creating a temporary work branch {branchName}", branchName);

                originalBranch = repo.Head.FriendlyName;
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

    protected record DependencyUpdate(
        SourceMapping Mapping,
        string RemoteUri,
        string TargetRevision,
        string? TargetVersion,
        SourceMapping? Parent);
}
