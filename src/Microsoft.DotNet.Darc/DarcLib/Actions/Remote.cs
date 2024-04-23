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
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

namespace Microsoft.DotNet.DarcLib;

public sealed class Remote : IRemote
{
    private readonly IVersionDetailsParser _versionDetailsParser;
    private readonly DependencyFileManager _fileManager;
    private readonly IRemoteGitRepo _remoteGitClient;
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
        ILogger logger)
    {
        _logger = logger;
        _remoteGitClient = remoteGitClient;
        _versionDetailsParser = versionDetailsParser;
        _fileManager = new DependencyFileManager(remoteGitClient, _versionDetailsParser, _logger);
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
        CheckForValidGitClient();
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
        CheckForValidGitClient();
        _logger.LogInformation($"Getting reviews for pull request '{pullRequestUrl}'...");
        return await _remoteGitClient.GetLatestPullRequestReviewsAsync(pullRequestUrl);
    }

    public Task CreateOrUpdatePullRequestMergeStatusInfoAsync(string pullRequestUrl, IReadOnlyList<MergePolicyEvaluationResult> evaluations)
    {
        CheckForValidGitClient();
        return _remoteGitClient.CreateOrUpdatePullRequestMergeStatusInfoAsync(pullRequestUrl, evaluations);
    }

    public async Task<PrStatus> GetPullRequestStatusAsync(string pullRequestUrl)
    {
        CheckForValidGitClient();
        _logger.LogInformation($"Getting the status of pull request '{pullRequestUrl}'...");

        PrStatus status = await _remoteGitClient.GetPullRequestStatusAsync(pullRequestUrl);

        _logger.LogInformation($"Status of pull request '{pullRequestUrl}' is '{status}'");

        return status;
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
        CheckForValidGitClient();
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
        CheckForValidGitClient();
        XmlDocument nugetConfig = await _fileManager.ReadNugetConfigAsync(repoUri, commit);
        return _fileManager.GetPackageSources(nugetConfig).Select(nameAndFeed => nameAndFeed.feed);
    }

    /// <summary>
    ///     Commit a set of updated dependencies to a repository
    /// </summary>
    /// <param name="repoUri">Repository to update</param>
    /// <param name="branch">Branch of <paramref name="repoUri"/> to update.</param>
    /// <param name="remoteFactory">Remote factory for obtaining common script files from arcade</param>
    /// <param name="itemsToUpdate">Dependencies that need updating.</param>
    /// <param name="message">Commit message.</param>
    /// <returns>Async task.</returns>
    public async Task<List<GitFile>> CommitUpdatesAsync(
        string repoUri,
        string branch,
        IRemoteFactory remoteFactory,
        IBasicBarClient barClient,
        List<DependencyDetail> itemsToUpdate,
        string message)
    {
        CheckForValidGitClient();

        List<DependencyDetail> oldDependencies = [.. await GetDependenciesAsync(repoUri, branch)];
        var locationResolver = new AssetLocationResolver(barClient);
        await locationResolver.AddAssetLocationToDependenciesAsync(oldDependencies);

        // If we are updating the arcade sdk we need to update the eng/common files
        // and the sdk versions in global.json
        DependencyDetail arcadeItem = itemsToUpdate.GetArcadeUpdate();
            
        SemanticVersion targetDotNetVersion = null;
        bool mayNeedArcadeUpdate = (arcadeItem != null && repoUri != arcadeItem.RepoUri);

        if (mayNeedArcadeUpdate)
        {
            IDependencyFileManager arcadeFileManager = await remoteFactory.GetDependencyFileManagerAsync(arcadeItem.RepoUri, _logger);
            targetDotNetVersion = await arcadeFileManager.ReadToolsDotnetVersionAsync(arcadeItem.RepoUri, arcadeItem.Commit);
        }

        GitFileContentContainer fileContainer = await _fileManager.UpdateDependencyFiles(
            itemsToUpdate,
            sourceDependency: null,
            repoUri,
            branch,
            oldDependencies,
            targetDotNetVersion);

        List<GitFile> filesToCommit = [];

        if (mayNeedArcadeUpdate)
        {
            // Files in the source arcade repo. We use the remote factory because the
            // arcade repo may be in github while this remote is targeted at AzDO.
            IRemote arcadeRemote = await remoteFactory.GetRemoteAsync(arcadeItem.RepoUri, _logger);
            List<GitFile> engCommonFiles = await arcadeRemote.GetCommonScriptFilesAsync(arcadeItem.RepoUri, arcadeItem.Commit);
            filesToCommit.AddRange(engCommonFiles);

            // Files in the target repo
            string latestCommit = await _remoteGitClient.GetLastCommitShaAsync(repoUri, branch);
            List<GitFile> targetEngCommonFiles = await GetCommonScriptFilesAsync(repoUri, latestCommit);

            var deletedFiles = new List<string>();

            foreach (GitFile file in targetEngCommonFiles)
            {
                if (!engCommonFiles.Where(f => f.FilePath == file.FilePath).Any())
                {
                    deletedFiles.Add(file.FilePath);
                    // This is a file in the repo's eng/common folder that isn't present in Arcade at the
                    // requested SHA so delete it during the update.
                    // GitFile instances do not have public setters since we insert/retrieve them from an
                    // In-memory cache and we don't want anything to modify the cached references,
                    // so add a copy with a Delete FileOperation.
                    filesToCommit.Add(new GitFile(
                        file.FilePath,
                        file.Content,
                        file.ContentEncoding,
                        file.Mode,
                        GitFileOperation.Delete));
                }
            }

            if (deletedFiles.Count > 0)
            {
                _logger.LogInformation($"Dependency update from Arcade commit {arcadeItem.Commit} to {repoUri} " +
                                       $"on branch {branch}@{latestCommit} will delete files in eng/common." +
                                       $" Source file count: {engCommonFiles.Count}, Target file count: {targetEngCommonFiles.Count}." +
                                       $" Deleted files: {string.Join(Environment.NewLine, deletedFiles)}");
            }
        }

        filesToCommit.AddRange(fileContainer.GetFilesToCommit());

        await _remoteGitClient.CommitFilesAsync(filesToCommit, repoUri, branch, message);

        return filesToCommit;
    }

    public Task<PullRequest> GetPullRequestAsync(string pullRequestUri)
    {
        CheckForValidGitClient();
        return _remoteGitClient.GetPullRequestAsync(pullRequestUri);
    }

    public Task<string> CreatePullRequestAsync(string repoUri, PullRequest pullRequest)
    {
        CheckForValidGitClient();
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
        CheckForValidGitClient();

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
        CheckForValidGitClient();
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
        CheckForValidGitClient();
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
        string name = null)
    {
        CheckForValidGitClient();
        VersionDetails versionDetails = await _fileManager.ParseVersionDetailsXmlAsync(repoUri, branchOrCommit);
        return versionDetails.Dependencies
            .Where(dependency => string.IsNullOrEmpty(name) || dependency.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
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
        CheckForValidGitClient();
        await _remoteGitClient.CloneAsync(repoUri, commit, targetDirectory, checkoutSubmodules, gitDirectory);
    }

    /// <summary>
    ///     Called prior to operations requiring GIT.  Throws if a git client isn't available;
    /// </summary>
    private void CheckForValidGitClient()
    {
        if (_remoteGitClient == null)
        {
            throw new ArgumentException("Must supply a valid GitHub/Azure DevOps PAT");
        }
    }

    public async Task<List<GitFile>> GetCommonScriptFilesAsync(string repoUri, string commit)
    {
        CheckForValidGitClient();
        _logger.LogInformation("Generating commits for script files");

        List<GitFile> files = await _remoteGitClient.GetFilesAtCommitAsync(repoUri, commit, Constants.CommonScriptFilesPath);

        _logger.LogInformation("Generating commits for script files succeeded!");

        return files;
    }
}
