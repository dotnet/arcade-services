// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

namespace Microsoft.DotNet.DarcLib;

public sealed class Remote : IRemote
{
    private readonly IVersionDetailsParser _versionDetailsParser;
    private readonly DependencyFileManager _fileManager;
    private readonly IRemoteGitRepo _remoteGitClient;
    private readonly ISourceMappingParser _sourceMappingParser;
    private readonly IRemoteFactory _remoteFactory;
    private readonly IAssetLocationResolver _locationResolver;
    private readonly IRedisCacheClient _cache;
    IGitRepoFactory _gitRepoFactory;
    private readonly ILogger _logger;

    //[DependencyUpdate]: <> (Begin)
    //- **Updates**:
    //- **Foo**: from to 1.2.0
    //- **Bar**: from to 2.2.0
    //[DependencyUpdate]: <> (End)
    private static readonly Regex DependencyUpdatesPattern =
        new(@"\[DependencyUpdate\]: <> \(Begin\)([^\[]+)\[DependencyUpdate\]: <> \(End\)");

    public Remote(
        IRemoteGitRepo remoteGitClient,
        IVersionDetailsParser versionDetailsParser,
        ISourceMappingParser sourceMappingParser,
        IRemoteFactory remoteFactory,
        IAssetLocationResolver locationResolver,
        IRedisCacheClient cacheClient,
        IGitRepoFactory gitRepoFactory,
        ILogger logger)
    {
        _logger = logger;
        _remoteGitClient = remoteGitClient;
        _versionDetailsParser = versionDetailsParser;
        _sourceMappingParser = sourceMappingParser;
        _remoteFactory = remoteFactory;
        _locationResolver = locationResolver;
        _fileManager = new DependencyFileManager(remoteGitClient, _versionDetailsParser, _logger);
        _gitRepoFactory = gitRepoFactory;
        _cache = cacheClient;
    }

    public async Task CreateNewBranchAsync(string repoUri, string baseBranch, string newBranch)
    {
        await _remoteGitClient.CreateBranchAsync(repoUri, newBranch, baseBranch);
    }

    public async Task DeleteBranchAsync(string repoUri, string branch)
    {
        _logger.LogInformation("Deleting branch '{branch}' from repo '{repoUri}'", branch, repoUri);
        await _remoteGitClient.DeleteBranchAsync(repoUri, branch);
    }

    public Task<bool> BranchExistsAsync(string repoUri, string branch)
    {
        _logger.LogInformation("Checking if branch '{branch}' exists in '{repoUri}'", branch, repoUri);
        return _remoteGitClient.DoesBranchExistAsync(repoUri, branch);
    }

    /// <summary>
    ///     Get a list of pull request checks.
    /// </summary>
    /// <param name="pullRequestUrl">Url of pull request</param>
    /// <returns>List of pull request checks</returns>
    public async Task<IEnumerable<Check>> GetPullRequestChecksAsync(string pullRequestUrl)
    {
        _logger.LogInformation($"Getting status checks for pull request '{pullRequestUrl}'...");
        return await _remoteGitClient.GetPullRequestChecksAsync(pullRequestUrl);
    }

    /// <summary>
    ///     Get a list of pull request reviews.
    /// </summary>
    /// <param name="pullRequestUrl">Url of pull request</param>
    /// <returns>List of pull request checks</returns>
    public async Task<IEnumerable<Review>> GetPullRequestReviewsAsync(string pullRequestUrl)
    {
        _logger.LogInformation($"Getting reviews for pull request '{pullRequestUrl}'...");
        return await _remoteGitClient.GetLatestPullRequestReviewsAsync(pullRequestUrl);
    }

    public Task CreateOrUpdatePullRequestMergeStatusInfoAsync(string pullRequestUrl, IReadOnlyCollection<MergePolicyEvaluationResult> evaluations)
    {
        return _remoteGitClient.CreateOrUpdatePullRequestMergeStatusInfoAsync(pullRequestUrl, evaluations);
    }

    public Task UpdatePullRequestAsync(string pullRequestUri, PullRequest pullRequest)
    {
        return _remoteGitClient.UpdatePullRequestAsync(pullRequestUri, pullRequest);
    }

    /// <summary>
    ///     Delete a Pull Request branch
    /// </summary>
    /// <param name="pullRequestUri">URI of pull request to delete branch for</param>
    /// <returns>Async task</returns>
    public async Task DeletePullRequestBranchAsync(string pullRequestUri)
    {
        try
        {
            await _remoteGitClient.DeletePullRequestBranchAsync(pullRequestUri);
        }
        catch (Exception e)
        {
            throw new DarcException("Failed to delete head branch for pull request {pullRequestUri}", e);
        }
    }

    /// <summary>
    /// Merges pull request for a dependency update  
    /// </summary>
    public async Task MergeDependencyPullRequestAsync(string pullRequestUrl, MergePullRequestParameters parameters)
    {
        _logger.LogInformation($"Merging pull request '{pullRequestUrl}'...");

        var pr = await _remoteGitClient.GetPullRequestAsync(pullRequestUrl);
        var dependencyUpdate = DependencyUpdatesPattern.Matches(pr.Description).Select(x => x.Groups[1].Value.Trim().Replace("*", string.Empty));
        var commitMessage = $@"{pr.Title}
{string.Join("\r\n\r\n", dependencyUpdate)}";
        var commits = await _remoteGitClient.GetPullRequestCommitsAsync(pullRequestUrl);
        foreach (Commit commit in commits)
        {
            if (!commit.Author.Equals(Constants.DarcBotName))
            {
                commitMessage += $@"

 - {commit.Message}";
            }
        }

        await _remoteGitClient.MergeDependencyPullRequestAsync(pullRequestUrl,
            parameters ?? new MergePullRequestParameters(), commitMessage);

        _logger.LogInformation($"Merging pull request '{pullRequestUrl}' succeeded!");
    }

    public async Task<IEnumerable<string>> GetPackageSourcesAsync(string repoUri, string commit)
    {
        (_, XmlDocument nugetConfig) = await _fileManager.ReadNugetConfigAsync(repoUri, commit);
        return _fileManager.GetPackageSources(nugetConfig).Select(nameAndFeed => nameAndFeed.feed);
    }

    public async Task CommitUpdatesAsync(
        List<GitFile> filesToCommit,
        string repoUri,
        string branch,
        string message) =>
            await _remoteGitClient.CommitFilesAsync(filesToCommit, repoUri, branch, message);

    public async Task CommitUpdatesWithNoCloningAsync(
        List<GitFile> filesToCommit,
        string repoUri,
        string branch,
        string message) =>
            await _remoteGitClient.CommitFilesWithNoCloningAsync(filesToCommit, repoUri, branch, message);


    public async Task<List<GitFile>> GetUpdatedDependencyFiles(
        string targetRepo,
        string branch,
        List<DependencyDetail> itemsToUpdate,
        UnixPath relativeDependencyBasePath = null)
    {
        (var targetRepoIsVmr, var targetDotNetVersion) = await GetDotNetVersionInVmrOrRepo(targetRepo, branch);

        List<DependencyDetail> oldDependencies = [.. await GetDependenciesAsync(targetRepo, branch, relativeBasePath: relativeDependencyBasePath)];
        await _locationResolver.AddAssetLocationToDependenciesAsync(oldDependencies);

        var updatedDependencyFiles = await _fileManager.UpdateDependencyFiles(
            itemsToUpdate,
            sourceDependency: null,
            targetRepo,
            branch,
            oldDependencies,
            targetDotNetVersion,
            relativeBasePath: relativeDependencyBasePath);

        var updatedEngCommonFiles = await GetUpdatedCommonScriptFilesAsync(
            targetRepo,
            branch,
            targetRepoIsVmr,
            itemsToUpdate.GetArcadeUpdate(),
            relativeDependencyBasePath);

        return [..updatedDependencyFiles.GetFilesToCommit(),
            ..updatedEngCommonFiles];
    }

    public async Task<List<GitFile>> GetUpdatedCommonScriptFilesAsync(
        string targetRepo,
        string branch,
        bool targetRepoIsVmr,
        DependencyDetail newArcadePackage,
        UnixPath targetDirectory = null)
    {
        if (newArcadePackage == null)
        {
            return [];
        }

        if (targetRepo == newArcadePackage.RepoUri &&
            (targetDirectory == UnixPath.Empty || targetDirectory == UnixPath.CurrentDir))
        {
            return []; // arcade->arcade and vmr->vmr subscriptions should not update root level eng/common
        }

        var newEngCommonFiles = await GetCommonScriptFilesByArcadePackage(newArcadePackage);

        newEngCommonFiles = [.. newEngCommonFiles
            .Select(f => new GitFile(
                targetDirectory / f.FilePath,
                f.Content,
                f.ContentEncoding,
                f.Mode,
                f.Operation))];

        var latestCommit = await _remoteGitClient.GetLastCommitShaAsync(targetRepo, branch);
        var targetRepoEngCommonFiles = await GetCommonScriptFilesAsync(
            targetRepo,
            latestCommit,
            baseDirectory: targetDirectory,
            stripBaseDirectory: false);

        var deletedFiles = CalculateFileDeletions(newEngCommonFiles, targetRepoEngCommonFiles);

        _logger.LogInformation(
            "Updating eng/common files from Arcade package with commit {commit} to {repoUri} on branch {branch}@{latestCommit} " +
            "Source file count: {sourceFileCount}, Target file count: {targetFileCount}. Deleted files: {deletedFiles}",
            newArcadePackage.Commit,
            targetRepo,
            branch,
            latestCommit,
            newEngCommonFiles.Count,
            targetRepoEngCommonFiles.Count,
            string.Join(Environment.NewLine, deletedFiles));

        return [.. newEngCommonFiles, .. deletedFiles];
    }

    private static List<GitFile> CalculateFileDeletions(List<GitFile> newFiles, List<GitFile> existingFiles)
    {
        var filesToKeep = newFiles
            .Select(f => f.FilePath)
            .ToHashSet(StringComparer.InvariantCultureIgnoreCase);

        List<GitFile> removedFiles = [.. existingFiles
        .Where(f => !filesToKeep.Contains(f.FilePath))
        .Select(f => new GitFile(
            f.FilePath,
            f.Content,
            f.ContentEncoding,
            f.Mode,
            GitFileOperation.Delete))];

        return removedFiles;
    }

    private async Task<List<GitFile>> GetCommonScriptFilesByArcadePackage(DependencyDetail arcadePackage)
    {
        IRemote sourceRepoRemote = await _remoteFactory.CreateRemoteAsync(arcadePackage.RepoUri);

        string sourceRepoUri = arcadePackage.RepoUri;
        IGitRepo sourceRepo = _gitRepoFactory.CreateClient(arcadePackage.RepoUri);

        var sourceRelativeBasePath = await sourceRepo.IsRepoVmrAsync(sourceRepoUri)
            ? VmrInfo.ArcadeRepoDir
            : null;

        return await sourceRepoRemote.GetCommonScriptFilesAsync(
            arcadePackage.RepoUri,
            arcadePackage.Commit,
            baseDirectory: sourceRelativeBasePath,
            stripRelativePath: true);
    }

    /// <summary>
    /// Gets the dotnet version of a repo, whether it is a product repo, or inside the VMR
    /// </summary>
    private async Task<(bool isVmr, SemanticVersion version)> GetDotNetVersionInVmrOrRepo(
        string repoUri,
        string commitSha)
    {
        SemanticVersion version;
        IDependencyFileManager arcadeFileManager = await _remoteFactory.CreateDependencyFileManagerAsync(repoUri);
        try
        {
            // First try to fetch it as if it was the VMR
            version = await arcadeFileManager.ReadToolsDotnetVersionAsync(repoUri, commitSha, VmrInfo.ArcadeRepoDir);
            return (true, version);
        }
        catch (DependencyFileNotFoundException)
        {
            // global.json not found in src/arcade meaning that the source repo is not the VMR
            version = await arcadeFileManager.ReadToolsDotnetVersionAsync(repoUri, commitSha);
            return (false, version);
        }
    }

    public Task<PullRequest> GetPullRequestAsync(string pullRequestUri)
    {
        return _remoteGitClient.GetPullRequestAsync(pullRequestUri);
    }

    public Task<PullRequest> CreatePullRequestAsync(string repoUri, PullRequest pullRequest)
    {
        return _remoteGitClient.CreatePullRequestAsync(repoUri, pullRequest);
    }

    /// <summary>
    ///     Diff two commits in a repository and return information about them.
    /// </summary>
    /// <param name="repoUri">Repository uri</param>
    /// <param name="baseVersion">Base version</param>
    /// <param name="targetVersion">Target version</param>
    /// <returns>Diff information</returns>
    public async Task<GitDiff> GitDiffAsync(string repoUri, string baseVersion, string targetVersion)
    {

        // If base and target are the same, return no diff
        if (baseVersion.Equals(targetVersion, StringComparison.OrdinalIgnoreCase))
        {
            return GitDiff.NoDiff(baseVersion);
        }

        return await _remoteGitClient.GitDiffAsync(repoUri, baseVersion, targetVersion);
    }

    /// <summary>
    /// Checks that a repository exists
    /// </summary>
    /// <param name="repoUri">Repository uri</param>
    /// <returns>True if the repository exists, false otherwise.</returns>
    public async Task<bool> RepositoryExistsAsync(string repoUri)
    {
        if (string.IsNullOrWhiteSpace(repoUri) || _remoteGitClient == null)
        {
            return false;
        }

        return await _remoteGitClient.RepoExistsAsync(repoUri);
    }

    /// <summary>
    ///     Get the latest commit in a branch
    /// </summary>
    /// <param name="repoUri">Remote repository</param>
    /// <param name="branch">Branch</param>
    /// <returns>Latest commit</returns>
    public Task<string> GetLatestCommitAsync(string repoUri, string branch)
    {
        return _remoteGitClient.GetLastCommitShaAsync(repoUri, branch);
    }

    /// <summary>
    ///     Get a commit in a repo 
    /// </summary>
    /// <param name="repoUri">Repository URI</param>
    /// <param name="sha">Sha of the commit</param>
    /// <returns>Return the commit matching the specified sha. Null if no commit were found.</returns>
    public Task<Commit> GetCommitAsync(string repoUri, string sha)
    {
        return _remoteGitClient.GetCommitAsync(repoUri, sha);
    }

    /// <summary>
    ///     Get the list of dependencies in the specified repo and branch/commit
    /// </summary>
    /// <param name="repoUri">Repository to get dependencies from</param>
    /// <param name="branchOrCommit">Commit to get dependencies at</param>
    /// <param name="name">Optional name of specific dependency to get information on</param>
    /// <returns>Matching dependency information.</returns>
    public async Task<IEnumerable<DependencyDetail>> GetDependenciesAsync(string repoUri,
        string branchOrCommit,
        string name = null,
        UnixPath relativeBasePath = null)
    {
        VersionDetails versionDetails = await _fileManager.ParseVersionDetailsXmlAsync(repoUri, branchOrCommit, relativeBasePath: relativeBasePath);
        return versionDetails.Dependencies
            .Where(dependency => string.IsNullOrEmpty(name) || dependency.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<SourceDependency> GetSourceDependencyAsync(string repoUri, string branch)
    {
        VersionDetails versionDetails = await _fileManager.ParseVersionDetailsXmlAsync(repoUri, branch);
        return versionDetails.Source;
    }


    /// <summary>
    ///     Clone a remote repo
    /// </summary>
    /// <param name="repoUri">Repository to clone</param>
    /// <param name="commit">Branch, commit, or tag to checkout</param>
    /// <param name="targetDirectory">Directory to clone to</param>
    /// <param name="checkoutSubmodules">Indicates whether submodules should be checked out as well</param>
    /// <param name="gitDirectory">Location for the .git directory</param>
    public async Task CloneAsync(string repoUri, string commit, string targetDirectory, bool checkoutSubmodules, string gitDirectory = null)
    {
        await _remoteGitClient.CloneAsync(repoUri, commit, targetDirectory, checkoutSubmodules, gitDirectory);
    }

    /// </summary>
    /// <param name="repoUri">Uri of the repository</param>
    /// <param name="commit">Commit at which to fetch the files</param>
    /// <param name="baseDirectory">Relative path from repo root where the eng folder is located (eg: `src/arcade`)</param>
    /// <param name="stripBaseDirectory">Strip the file paths of the base directory, such that they start with `eng/common/...`</param>
    public async Task<List<GitFile>> GetCommonScriptFilesAsync(
        string repoUri,
        string commit,
        LocalPath baseDirectory = null,
        bool stripBaseDirectory = false)
    {
        string path = baseDirectory == null
            ? Constants.CommonScriptFilesPath
            : baseDirectory / Constants.CommonScriptFilesPath;

        List<GitFile> files = await _remoteGitClient.GetFilesAtCommitAsync(repoUri, commit, path);

        if (stripBaseDirectory)
        {
            files = [.. files.Select(f => new GitFile(
                f.FilePath.TrimStart(baseDirectory),
                f.Content,
                f.ContentEncoding,
                f.Mode,
                f.Operation))];
        }

        _logger.LogInformation("Fetched common script files from repo {RepoUri} at commit {Commit}, "
            + "at relative path {RelativeBasePath}",
            repoUri,
            commit,
            baseDirectory);

        return files;
    }

    public async Task<List<GitFile>> GetFilesAtCommitAsync(string repoUri, string commit, string path)
        => await _remoteGitClient.GetFilesAtCommitAsync(repoUri, commit, path);

    public async Task<List<string>> ListFilesAtCommitAsync(string repoUri, string commit, string path)
        => await _remoteGitClient.ListFilesAtCommitAsync(repoUri, commit, path);

    public async Task CommentPullRequestAsync(string pullRequestUri, string comment)
    {
        await _remoteGitClient.CommentPullRequestAsync(pullRequestUri, comment);
    }

    public async Task<List<string>> GetPullRequestCommentsAsync(string pullRequestUrl)
    {
        return await _remoteGitClient.GetPullRequestCommentsAsync(pullRequestUrl);
    }


    public async Task<SourceManifest> GetSourceManifestAsync(string vmrUri, string branchOrCommit)
    {
        var fileContent = await _remoteGitClient.GetFileContentsAsync(
            VmrInfo.DefaultRelativeSourceManifestPath,
            vmrUri,
            branchOrCommit);
        return SourceManifest.FromJson(fileContent);
    }

    public async Task<string> GetFileContentsAsync(string filePath, string repoUri, string branch)
        => await _remoteGitClient.GetFileContentsAsync(filePath, repoUri, branch);

    public async Task<SourceManifest> GetSourceManifestAtCommitAsync(string vmrUri, string commitSha)
    {
        if (!StringUtils.IsValidLongCommitSha(commitSha))
        {
            throw new ArgumentException($"The provided commit SHA `{commitSha}` is either not of length 40 or contains illegal characters.", nameof(commitSha));
        }

        var cachedManifestData = await _cache.TryGetAsync<SourceManifestWrapper>(commitSha);

        if (cachedManifestData != null)
        {
            var cachedManifest = SourceManifestWrapper.ToSourceManifest(cachedManifestData);
            return cachedManifest;
        }

        var sourceManifest = await GetSourceManifestAsync(vmrUri, commitSha);

        await _cache.TrySetAsync(commitSha, SourceManifest.ToWrapper(sourceManifest));

        return sourceManifest;
    }

    public async Task<IReadOnlyCollection<SourceMapping>> GetSourceMappingsAsync(string vmrUri, string branch)
    {
        var fileContent = await _remoteGitClient.GetFileContentsAsync(
            VmrInfo.DefaultRelativeSourceMappingsPath,
            vmrUri,
            branch);
        return _sourceMappingParser.ParseMappingsFromJson(fileContent);
    }

    public async Task<IReadOnlyCollection<string>> GetGitTreeNames(string path, string repoUri, string branch)
    {
        return await _remoteGitClient.GetGitTreeNames(path, repoUri, branch);
    }
}
