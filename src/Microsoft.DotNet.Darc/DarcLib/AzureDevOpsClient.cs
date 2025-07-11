// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Maestro.Common.AzureDevOpsTokens;
using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.AzureDevOps;
using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Microsoft.DotNet.DarcLib;

public class AzureDevOpsClient : RemoteRepoBase, IRemoteGitRepo, IAzureDevOpsClient
{
    private const string DefaultApiVersion = "5.0";

    private const int MaxPullRequestDescriptionLength = 4000;

    private const string RefsHeadsPrefix = "refs/heads/";

    private static readonly string AzureDevOpsHostPattern = @"dev\.azure\.com\";

    private static readonly string CommentMarker =
        "\n\n[//]: # (This identifies this comment as a Maestro++ comment)\n";

    private static readonly Regex RepositoryUriPattern = new(
        $"^https://{AzureDevOpsHostPattern}/(?<account>[a-zA-Z0-9]+)/(?<project>[a-zA-Z0-9-]+)/_git/(?<repo>[a-zA-Z0-9-\\.]+)");

    private static readonly Regex LegacyRepositoryUriPattern = new(
        @"^https://(?<account>[a-zA-Z0-9]+)\.visualstudio\.com/(?<project>[a-zA-Z0-9-]+)/_git/(?<repo>[a-zA-Z0-9-\.]+)");

    private static readonly Regex PullRequestApiUriPattern = new(
        $"^https://{AzureDevOpsHostPattern}/(?<account>[a-zA-Z0-9]+)/(?<project>[a-zA-Z0-9-]+)/_apis/git/repositories/(?<repo>[a-zA-Z0-9-\\.]+)/pullRequests/(?<id>\\d+)");

    // Azure DevOps uses this id when creating a new branch as well as when deleting a branch
    private static readonly string BaseObjectId = "0000000000000000000000000000000000000000";

    private readonly IAzureDevOpsTokenProvider _tokenProvider;
    private readonly ILogger _logger;
    private readonly JsonSerializerSettings _serializerSettings;
    private readonly Dictionary<(string, string, string), string> _gitRefCommitCache;

    public AzureDevOpsClient(IAzureDevOpsTokenProvider tokenProvider, IProcessManager processManager, ILogger logger)
        : this(tokenProvider, processManager, logger, null)
    {
    }

    public AzureDevOpsClient(IAzureDevOpsTokenProvider tokenProvider, IProcessManager processManager, ILogger logger, string temporaryRepositoryPath)
        : base(tokenProvider, processManager, temporaryRepositoryPath, null, logger)
    {
        _tokenProvider = tokenProvider;
        _logger = logger;
        _serializerSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore
        };
        _gitRefCommitCache = [];
    }

    public bool AllowRetries { get; set; } = true;

    /// <summary>
    /// Retrieve the contents of a text file in a repo on a specific branch
    /// </summary>
    /// <param name="filePath">Path to file within the repo</param>
    /// <param name="repoUri">Repository url</param>
    /// <param name="branch">Branch or commit</param>
    /// <returns>Content of file</returns>
    public Task<string> GetFileContentsAsync(string filePath, string repoUri, string branch)
    {
        (string accountName, string projectName, string repoName) = ParseRepoUri(repoUri);

        return GetFileContentsAsync(accountName, projectName, repoName, filePath, branch);
    }

    private static readonly List<string> VersionTypes = ["branch", "commit", "tag"];
    /// <summary>
    ///     Retrieve the contents of a text file in a repo on a specific branch
    /// </summary>
    /// <param name="accountName">Azure DevOps account</param>
    /// <param name="projectName">Azure DevOps project</param>
    /// <param name="repoName">Azure DevOps repo</param>
    /// <param name="filePath">Path to file</param>
    /// <param name="branchOrCommit">Branch</param>
    /// <returns>Contents of file as string</returns>
    private async Task<string> GetFileContentsAsync(
        string accountName,
        string projectName,
        string repoName,
        string filePath,
        string branchOrCommit)
    {
        _logger.LogInformation(
            $"Getting the contents of file '{filePath}' from repo '{accountName}/{projectName}/{repoName}' in branch/commit '{branchOrCommit}'...");

        // The AzDO REST API currently does not transparently handle commits vs. branches vs. tags.
        // You really need to know whether you're talking about a commit or branch or tag
        // when you ask the question. Avoid this issue for now by first checking branch (most common)
        // then if it 404s, check commit and then tag.
        HttpRequestException lastException = null;
        foreach (var versionType in VersionTypes)
        {
            try
            {
                JObject content = await ExecuteAzureDevOpsAPIRequestAsync(
                    HttpMethod.Get,
                    accountName,
                    projectName,
                    $"_apis/git/repositories/{repoName}/items?path={filePath}&versionType={versionType}&version={branchOrCommit}&includeContent=true",
                    _logger,
                    // Don't log the failure so users don't get confused by 404 messages popping up in expected circumstances.
                    logFailure: false);
                return content["content"].ToString();
            }
            catch (HttpRequestException reqEx) when (reqEx.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
            {
                // Continue
                lastException = reqEx;
            }
        }

        throw new DependencyFileNotFoundException(filePath, $"{repoName}", branchOrCommit, lastException);
    }

    /// <summary>
    /// Create a new branch in a repository
    /// </summary>
    /// <param name="repoUri">Repo to create a branch in</param>
    /// <param name="newBranch">New branch name</param>
    /// <param name="baseBranch">Base of new branch</param>
    public async Task CreateBranchAsync(string repoUri, string newBranch, string baseBranch)
    {
        (string accountName, string projectName, string repoName) = ParseRepoUri(repoUri);

        var azureDevOpsRefs = new List<AzureDevOpsRef>();
        string latestSha = await GetLastCommitShaAsync(accountName, projectName, repoName, baseBranch);

        JObject content = await ExecuteAzureDevOpsAPIRequestAsync(
            HttpMethod.Get,
            accountName,
            projectName,
            $"_apis/git/repositories/{repoName}/refs/heads/{newBranch}",
            _logger,
            retryCount: 0);

        AzureDevOpsRef azureDevOpsRef;

        // Azure DevOps doesn't fail with a 404 if a branch does not exist, it just returns an empty response object...
        if (content["count"].ToObject<int>() == 0)
        {
            _logger.LogInformation($"'{newBranch}' branch doesn't exist. Creating it...");

            azureDevOpsRef = new AzureDevOpsRef($"refs/heads/{newBranch}", latestSha, BaseObjectId);
            azureDevOpsRefs.Add(azureDevOpsRef);
        }
        else
        {
            _logger.LogInformation(
                $"Branch '{newBranch}' exists, making sure it is in sync with '{baseBranch}'...");

            string oldSha = await GetLastCommitShaAsync(repoName, $"{newBranch}");

            azureDevOpsRef = new AzureDevOpsRef($"refs/heads/{newBranch}", latestSha, oldSha);
            azureDevOpsRefs.Add(azureDevOpsRef);
        }

        string body = JsonConvert.SerializeObject(azureDevOpsRefs, _serializerSettings);

        await ExecuteAzureDevOpsAPIRequestAsync(
            HttpMethod.Post,
            accountName,
            projectName,
            $"_apis/git/repositories/{repoName}/refs",
            _logger,
            body);
    }

    /// <summary>
    /// Deletes a branch in a repository
    /// </summary>
    /// <param name="repoUri">Repository Uri</param>
    /// <param name="branch">Branch to delete</param>
    /// <returns>Async task</returns>
    public async Task DeleteBranchAsync(string repoUri, string branch)
    {
        (string accountName, string projectName, string repoName) = ParseRepoUri(repoUri);

        await DeleteBranchAsync(accountName, projectName, repoName, branch);
    }

    /// <summary>
    ///     Finds out whether a branch exists in the target repo.
    /// </summary>
    /// <param name="repoUri">Repository to find the branch in</param>
    /// <param name="branch">Branch to find</param>
    public async Task<bool> DoesBranchExistAsync(string repoUri, string branch)
    {
        branch = GitHelpers.NormalizeBranchName(branch);
        (string accountName, string projectName, string repoName) = ParseRepoUri(repoUri);

        JObject content = await ExecuteAzureDevOpsAPIRequestAsync(
            HttpMethod.Get,
            accountName,
            projectName,
            $"_apis/git/repositories/{repoName}/refs?filter=heads/{branch}",
            _logger,
            versionOverride: "7.0",
            logFailure: false);

        var refs = ((JArray)content["value"]).ToObject<List<AzureDevOpsRef>>();
        return refs.Any(refs => refs.Name == $"refs/heads/{branch}");
    }

    /// <summary>
    /// Deletes a branch in a repository
    /// </summary>
    /// <param name="accountName">Azure DevOps Account</param>
    /// <param name="projectName">Azure DevOps project</param>
    /// <param name="repoName">Name of the repository</param>
    /// <param name="branch">Brach to delete</param>
    /// <returns>Async Task</returns>
    private async Task DeleteBranchAsync(string accountName, string projectName, string repoName, string branch)
    {
        string latestSha = await GetLastCommitShaAsync(accountName, projectName, repoName, branch);

        var azureDevOpsRefs = new List<AzureDevOpsRef>();
        var azureDevOpsRef = new AzureDevOpsRef($"refs/heads/{branch}", BaseObjectId, latestSha);
        azureDevOpsRefs.Add(azureDevOpsRef);

        string body = JsonConvert.SerializeObject(azureDevOpsRefs, _serializerSettings);

        await ExecuteAzureDevOpsAPIRequestAsync(
            HttpMethod.Post,
            accountName,
            projectName,
            $"_apis/git/repositories/{repoName}/refs",
            _logger,
            body);
    }

    /// <summary>
    ///     Search pull requests matching the specified criteria
    /// </summary>
    /// <param name="repoUri">URI of repo containing the pull request</param>
    /// <param name="pullRequestBranch">Source branch for PR</param>
    /// <param name="status">Current PR status</param>
    /// <param name="keyword">Keyword</param>
    /// <param name="author">Author</param>
    /// <returns>List of pull requests matching the specified criteria</returns>
    public async Task<IEnumerable<int>> SearchPullRequestsAsync(
        string repoUri,
        string pullRequestBranch,
        PrStatus status,
        string keyword = null,
        string author = null)
    {
        (string accountName, string projectName, string repoName) = ParseRepoUri(repoUri);
        var query = new StringBuilder();
        var prStatus = status switch
        {
            PrStatus.Open => AzureDevOpsPrStatus.Active,
            PrStatus.Closed => AzureDevOpsPrStatus.Abandoned,
            PrStatus.Merged => AzureDevOpsPrStatus.Completed,
            _ => AzureDevOpsPrStatus.None,
        };
        query.Append($"searchCriteria.sourceRefName=refs/heads/{pullRequestBranch}&searchCriteria.status={prStatus.ToString().ToLower()}");

        if (!string.IsNullOrEmpty(keyword))
        {
            _logger.LogInformation(
                "A keyword was provided but Azure DevOps doesn't support searching for PRs based on keywords and it won't be used...");
        }

        if (!string.IsNullOrEmpty(author))
        {
            query.Append($"&searchCriteria.creatorId={author}");
        }

        JObject content = await ExecuteAzureDevOpsAPIRequestAsync(
            HttpMethod.Get,
            accountName,
            projectName,
            $"_apis/git/repositories/{repoName}/pullrequests?{query}",
            _logger);

        var values = JArray.Parse(content["value"].ToString());
        IEnumerable<int> prs = values.Select(r => r["pullRequestId"].ToObject<int>());

        return prs;
    }

    /// <summary>
    ///     Retrieve information on a specific pull request
    /// </summary>
    /// <param name="pullRequestUrl">Uri of the pull request</param>
    /// <returns>Information on the pull request.</returns>
    public async Task<PullRequest> GetPullRequestAsync(string pullRequestUrl)
    {
        (string accountName, string projectName, string repoName, int id) = ParsePullRequestUri(pullRequestUrl);

        using VssConnection connection = CreateVssConnection(accountName);
        using GitHttpClient client = await connection.GetClientAsync<GitHttpClient>();

        GitPullRequest pr = await client.GetPullRequestAsync(projectName, repoName, id);
        // Strip out the refs/heads prefix on BaseBranch and HeadBranch because almost
        // all of the other APIs we use do not support them (e.g. get an item at branch X).
        // At the time this code was written, the API always returned the refs with this prefix,
        // so verify this is the case.

        if (!pr.TargetRefName.StartsWith(RefsHeadsPrefix) || !pr.SourceRefName.StartsWith(RefsHeadsPrefix))
        {
            throw new NotImplementedException("Expected that source and target ref names returned from pull request API include refs/heads");
        }

        return new PullRequest
        {
            Title = pr.Title,
            Description = pr.Description,
            BaseBranch = pr.TargetRefName.Substring(RefsHeadsPrefix.Length),
            HeadBranch = pr.SourceRefName.Substring(RefsHeadsPrefix.Length),
            Status = pr.Status switch
            {
                PullRequestStatus.Active => PrStatus.Open,
                PullRequestStatus.Completed => PrStatus.Merged,
                PullRequestStatus.Abandoned => PrStatus.Closed,
                _ => PrStatus.None,
            },
            UpdatedAt = DateTimeOffset.UtcNow,
            TargetBranchCommitSha = pr.LastMergeTargetCommit.CommitId,
        };
    }

    /// <summary>
    ///     Create a new pull request
    /// </summary>
    /// <param name="repoUri">Repository URI</param>
    /// <param name="pullRequest">Pull request data</param>
    /// <returns>URL of new pull request</returns>
    public async Task<string> CreatePullRequestAsync(string repoUri, PullRequest pullRequest)
    {
        (string accountName, string projectName, string repoName) = ParseRepoUri(repoUri);

        using VssConnection connection = CreateVssConnection(accountName);
        using GitHttpClient client = await connection.GetClientAsync<GitHttpClient>();

        GitPullRequest createdPr = await client.CreatePullRequestAsync(
            new GitPullRequest
            {
                Title = pullRequest.Title,
                Description = TruncateDescriptionIfNeeded(pullRequest.Description),
                SourceRefName = RefsHeadsPrefix + pullRequest.HeadBranch,
                TargetRefName = RefsHeadsPrefix + pullRequest.BaseBranch,
            },
            projectName,
            repoName);

        return createdPr.Url;
    }

    /// <summary>
    ///     Update a pull request with new information
    /// </summary>
    /// <param name="pullRequestUri">Uri of pull request to update</param>
    /// <param name="pullRequest">Pull request info to update</param>
    /// <returns></returns>
    public async Task UpdatePullRequestAsync(string pullRequestUri, PullRequest pullRequest)
    {
        (string accountName, string projectName, string repoName, int id) = ParsePullRequestUri(pullRequestUri);

        using VssConnection connection = CreateVssConnection(accountName);
        using GitHttpClient client = await connection.GetClientAsync<GitHttpClient>();

        await client.UpdatePullRequestAsync(
            new GitPullRequest
            {
                Title = pullRequest.Title,
                Description = TruncateDescriptionIfNeeded(pullRequest.Description),
            },
            projectName,
            repoName,
            id);
    }

    /// <summary>
    /// Gets all the commits related to the pull request
    /// </summary>
    /// <param name="pullRequestUrl"></param>
    /// <returns>All the commits related to the pull request</returns>
    public async Task<IList<Commit>> GetPullRequestCommitsAsync(string pullRequestUrl)
    {
        (string accountName, string project, string repoName, int id) = ParsePullRequestUri(pullRequestUrl);
        using VssConnection connection = CreateVssConnection(accountName);
        using GitHttpClient client = await connection.GetClientAsync<GitHttpClient>();

        GitPullRequest pullRequest = await client.GetPullRequestAsync(project, repoName, id, includeCommits: true);

        IList<Commit> commits = new List<Commit>(pullRequest.Commits.Length);
        foreach (var commit in pullRequest.Commits)
        {
            commits.Add(new Commit(
                commit.Author.Name == "DotNet-Bot" ? Constants.DarcBotName : commit.Author.Name,
                commit.CommitId,
                commit.Comment));
        }

        return commits;
    }

    public async Task MergeDependencyPullRequestAsync(string pullRequestUrl, MergePullRequestParameters parameters,
        string mergeCommitMessage)
    {
        (string accountName, string projectName, string repoName, int id) = ParsePullRequestUri(pullRequestUrl);

        using VssConnection connection = CreateVssConnection(accountName);
        using GitHttpClient client = await connection.GetClientAsync<GitHttpClient>();
        var pullRequest = await client.GetPullRequestAsync(projectName, repoName, id, includeCommits: true);

        try
        {
            await client.UpdatePullRequestAsync(
                new GitPullRequest
                {
                    Status = PullRequestStatus.Completed,
                    CompletionOptions = new GitPullRequestCompletionOptions
                    {
                        MergeCommitMessage = mergeCommitMessage,
                        BypassPolicy = true,
                        BypassReason = "All required checks were successful",
                        SquashMerge = parameters.SquashMerge,
                        DeleteSourceBranch = parameters.DeleteSourceBranch
                    },
                    LastMergeSourceCommit = new GitCommitRef
                    { CommitId = pullRequest.LastMergeSourceCommit.CommitId, Comment = mergeCommitMessage }
                },
                projectName,
                repoName,
                id);
        }
        catch (Exception ex) when (
            ex.Message.StartsWith("The pull request needs a minimum number of approvals") ||
            ex.Message == "Proof of presence is required" ||
            ex.Message == "Failure while attempting to queue Build." ||
            ex.Message.Contains("Please re-approve the most recent pull request iteration"))
        {
            throw new PullRequestNotMergeableException(ex.Message);
        }
    }

    /// <summary>
    ///     Create a new comment, or update the last comment with an updated message,
    ///     if that comment was created by Darc.
    /// </summary>
    /// <param name="pullRequestUrl">Url of pull request</param>
    /// <param name="message">Message to post</param>
    /// <remarks>
    ///     Search through the pull request comment threads to find one who's *last* comment ends
    ///     in the comment marker. If the comment is found, update it, otherwise append a comment
    ///     to the first thread that has a comment marker for any comment.
    ///     Create a new thread if no comment markers were found.
    /// </remarks>
    private async Task CreateOrUpdatePullRequestCommentAsync(string pullRequestUrl, string message)
    {
        (string accountName, string projectName, string repoName, int id) = ParsePullRequestUri(pullRequestUrl);

        using VssConnection connection = CreateVssConnection(accountName);
        using GitHttpClient client = await connection.GetClientAsync<GitHttpClient>();

        var prComment = new Comment()
        {
            CommentType = CommentType.Text,
            Content = $"{message}{CommentMarker}"
        };

        // Search threads to find ones with comment markers.
        List<GitPullRequestCommentThread> commentThreads = await client.GetThreadsAsync(repoName, id);
        foreach (GitPullRequestCommentThread commentThread in commentThreads)
        {
            // Skip non-active and non-unknown threads.  Threads that are active may appear as unknown.
            if (commentThread.Status != CommentThreadStatus.Active && commentThread.Status != CommentThreadStatus.Unknown)
            {
                continue;
            }
            List<Comment> comments = await client.GetCommentsAsync(repoName, id, commentThread.Id);
            bool threadHasCommentWithMarker = comments.Any(comment => comment.CommentType == CommentType.Text && comment.Content.EndsWith(CommentMarker));
            if (threadHasCommentWithMarker)
            {
                // Check if last comment in that thread has the marker.
                Comment lastComment = comments.Last();
                if (lastComment.CommentType == CommentType.Text && lastComment.Content.EndsWith(CommentMarker))
                {
                    // Update comment
                    await client.UpdateCommentAsync(prComment, repoName, id, commentThread.Id, lastComment.Id);
                }
                else
                {
                    // Add a new comment to the end of the thread
                    await client.CreateCommentAsync(prComment, repoName, id, commentThread.Id);
                }
                return;
            }
        }

        // No threads found, create a new one with the comment
        var newCommentThread = new GitPullRequestCommentThread()
        {
            Comments = [ prComment ]
        };
        await client.CreateThreadAsync(newCommentThread, repoName, id);
    }

    public async Task CreateOrUpdatePullRequestMergeStatusInfoAsync(string pullRequestUrl, IReadOnlyCollection<MergePolicyEvaluationResult> evaluations)
    {
        await CreateOrUpdatePullRequestCommentAsync(pullRequestUrl,
            $"""
            ## Auto-Merge Status
            
            This pull request has not been merged because Maestro++ is waiting on the following merge policies.
            {string.Join(Environment.NewLine, evaluations.OrderBy(r => r.MergePolicyName).Select(DisplayPolicy))}
            """);
    }

    private string DisplayPolicy(MergePolicyEvaluationResult result)
    {
        if (result.Status == MergePolicyEvaluationStatus.Pending)
        {
            return $"- ❓ **{result.Title}** - {result.Message}";
        }

        if (result.Status == MergePolicyEvaluationStatus.DecisiveSuccess || result.Status == MergePolicyEvaluationStatus.TransientSuccess)
        {
            return $"- ✔️ **{result.MergePolicyDisplayName}** Succeeded"
                + (result.Title == null ? string.Empty: $" - {result.Title}");
        }

        return $"- ❌ **{result.MergePolicyDisplayName}** {result.Title} - {result.Message}";
    }

    /// <summary>
    ///     Retrieve a set of file under a specific path at a commit
    /// </summary>
    /// <param name="repoUri">Repository URI</param>
    /// <param name="commit">Commit to get files at</param>
    /// <param name="path">Path to retrieve files from</param>
    /// <returns>Set of files under <paramref name="path"/> at <paramref name="commit"/></returns>
    public async Task<List<GitFile>> GetFilesAtCommitAsync(string repoUri, string commit, string path)
    {
        var files = new List<GitFile>();

        _logger.LogInformation("Getting the contents of '{path}' in repo '{repoUri}' at '{commit}'",
            path,
            repoUri,
            commit);

        (string accountName, string projectName, string repoName) = ParseRepoUri(repoUri);

        JObject content = await ExecuteAzureDevOpsAPIRequestAsync(
            HttpMethod.Get,
            accountName,
            projectName,
            $"_apis/git/repositories/{repoName}/items?scopePath={path}&version={commit}&includeContent=true&versionType=commit&recursionLevel=full",
            _logger);
        List<AzureDevOpsItem> items = JsonConvert.DeserializeObject<List<AzureDevOpsItem>>(Convert.ToString(content["value"]));

        foreach (AzureDevOpsItem item in items)
        {
            if (!item.IsFolder)
            {
                if (!DependencyFileManager.DependencyFiles.Contains(item.Path))
                {
                    string fileContent = await GetFileContentsAsync(accountName, projectName, repoName, item.Path, commit);
                    var gitCommit = new GitFile(item.Path.TrimStart('/'), fileContent);
                    files.Add(gitCommit);
                }
            }
        }

        _logger.LogInformation("Getting the contents of '{path}' in repo '{repoUri}' at '{commit}' succeeded!",
            path,
            repoUri,
            commit);

        return files;
    }

    /// <summary>
    ///     Get the latest commit in a repo on the specific branch 
    /// </summary>
    /// <param name="repoUri">Repository uri</param>
    /// <param name="branch">Branch to retrieve the latest sha for</param>
    /// <returns>Latest sha. Null if no commits were found.</returns>
    public Task<string> GetLastCommitShaAsync(string repoUri, string branch)
    {
        (string accountName, string projectName, string repoName) = ParseRepoUri(repoUri);
        return GetLastCommitShaAsync(accountName, projectName, repoName, branch);
    }

    /// <summary>
    ///     Get the latest commit in a repo on the specific branch
    /// </summary>
    /// <param name="accountName">Azure DevOps account</param>
    /// <param name="projectName">Azure DevOps project</param>
    /// <param name="repoName">Azure DevOps repo</param>
    /// <param name="branch">Branch</param>
    /// <returns>Latest sha. Throws if there were not commits on <paramref name="branch"/></returns>
    private async Task<string> GetLastCommitShaAsync(string accountName, string projectName, string repoName, string branch)
    {
        try
        {
            JObject content = await ExecuteAzureDevOpsAPIRequestAsync(
                HttpMethod.Get,
                accountName,
                projectName,
                $"_apis/git/repositories/{repoName}/commits?branch={branch}",
                _logger);
            var values = JArray.Parse(content["value"].ToString());

            return values[0]["commitId"].ToString();
        }
        catch (HttpRequestException exc) when (exc.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>
    ///     Get a commit in a repo 
    /// </summary>
    /// <param name="repoUri">Repository URI</param>
    /// <param name="sha">Sha of the commit</param>
    /// <returns>Return the commit matching the specified sha. Null if no commit were found.</returns>
    public Task<Commit> GetCommitAsync(string repoUri, string sha)
    {
        (string accountName, string projectName, string repoName) = ParseRepoUri(repoUri);
        return GetCommitAsync(accountName, projectName, repoName, sha);
    }

    /// <summary>
    ///     Get a commit in a repo 
    /// </summary>
    /// <param name="owner">Owner of repo</param>
    /// <param name="repo">Repository name</param>
    /// <param name="sha">Sha of the commit</param>
    /// <returns>Return the commit matching the specified sha. Null if no commit were found.</returns>
    private async Task<Commit> GetCommitAsync(string accountName, string projectName, string repoName, string sha)
    {
        try
        {
            JObject content = await ExecuteAzureDevOpsAPIRequestAsync(
                HttpMethod.Get,
                accountName,
                projectName,
                $"_apis/git/repositories/{repoName}/commits/{sha}",
                _logger,
                versionOverride: "6.0");
            var values = JObject.Parse(content.ToString());
               
            return new Commit(values["author"]["name"].ToString(), sha, values["comment"].ToString());
        }
        catch (HttpRequestException exc) when (exc.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>
    ///     Diff two commits in a repository and return information about them.
    /// </summary>
    /// <param name="repoUri">Repository uri</param>
    /// <param name="baseCommit">Base version</param>
    /// <param name="targetCommit">Target version</param>
    /// <returns>Diff information</returns>
    public async Task<GitDiff> GitDiffAsync(string repoUri, string baseCommit, string targetCommit)
    {
        _logger.LogInformation(
            $"Diffing '{baseCommit}'->'{targetCommit}' in {repoUri}");
        (string accountName, string projectName, string repoName) = ParseRepoUri(repoUri);

        try
        {
            JObject content = await ExecuteAzureDevOpsAPIRequestAsync(
                HttpMethod.Get,
                accountName,
                projectName,
                $"_apis/git/repositories/{repoName}/diffs/commits?baseVersion={baseCommit}&baseVersionType=commit" +
                $"&targetVersion={targetCommit}&targetVersionType=commit",
                _logger);

            return new GitDiff()
            {
                BaseVersion = baseCommit,
                TargetVersion = targetCommit,
                Ahead = content["aheadCount"].Value<int>(),
                Behind = content["behindCount"].Value<int>(),
                Valid = true
            };
        }
        catch (HttpRequestException reqEx) when (reqEx.StatusCode == HttpStatusCode.NotFound)
        {
            return GitDiff.UnknownDiff();
        }
    }

    /// <summary>
    /// Retrieve the list of status checks on a PR.
    /// </summary>
    /// <param name="pullRequestUrl">Uri of pull request</param>
    /// <returns>List of status checks.</returns>
    public async Task<IList<Check>> GetPullRequestChecksAsync(string pullRequestUrl)
    {
        (string accountName, string projectName, _, int id) = ParsePullRequestUri(pullRequestUrl);

        string projectId = await GetProjectIdAsync(accountName, projectName);

        string artifactId = $"vstfs:///CodeReview/CodeReviewId/{projectId}/{id}";

        string statusesPath = $"_apis/policy/evaluations?artifactId={artifactId}";

        JObject content = await ExecuteAzureDevOpsAPIRequestAsync(HttpMethod.Get,
            accountName,
            projectName,
            statusesPath,
            _logger,
            versionOverride: "5.1-preview.1");

        var values = JArray.Parse(content["value"].ToString());

        IList<Check> statuses = [];
        foreach (JToken status in values)
        {
            bool isEnabled = status["configuration"]["isEnabled"].Value<bool>();

            if (isEnabled && Enum.TryParse(status["status"].ToString(), true, out AzureDevOpsCheckState state))
            {
                var checkState = state switch
                {
                    AzureDevOpsCheckState.Broken => CheckState.Error,
                    AzureDevOpsCheckState.Rejected => CheckState.Failure,
                    AzureDevOpsCheckState.Queued or AzureDevOpsCheckState.Running => CheckState.Pending,
                    AzureDevOpsCheckState.Approved => CheckState.Success,
                    _ => CheckState.None,
                };
                statuses.Add(
                    new Check(
                        checkState,
                        status["configuration"]["type"]["displayName"].ToString(),
                        status["configuration"]["url"].ToString()));
            }
        }

        return statuses;
    }

    /// <summary>
    /// Retrieve the list of reviews on a PR.
    /// </summary>
    /// <param name="pullRequestUrl">Uri of pull request</param>
    /// <returns>List of reviews.</returns>
    public async Task<IList<Review>> GetLatestPullRequestReviewsAsync(string pullRequestUrl)
    {
        (string accountName, string projectName, string repo, int id) = ParsePullRequestUri(pullRequestUrl);

        JObject content = await ExecuteAzureDevOpsAPIRequestAsync(
            HttpMethod.Get,
            accountName,
            projectName,
            $"_apis/git/repositories/{repo}/pullRequests/{id}/reviewers",
            _logger);

        var values = JArray.Parse(content["value"].ToString());

        IList<Review> reviews = [];
        foreach (JToken review in values)
        {
            // Azure DevOps uses an integral "vote" value to identify review state
            // from their documentation:
            // Vote on a pull request:
            // 10 - approved 5 - approved with suggestions 0 - no vote - 5 - waiting for author - 10 - rejected

            int vote = review["vote"].Value<int>();
            var reviewState = vote switch
            {
                10 => ReviewState.Approved,
                5 => ReviewState.Commented,
                0 => ReviewState.Pending,
                -5 => ReviewState.ChangesRequested,
                -10 => ReviewState.Rejected,
                _ => throw new NotImplementedException($"Unknown review vote {vote}"),
            };
            reviews.Add(new Review(reviewState, pullRequestUrl));
        }

        return reviews;
    }

    /// <summary>
    ///     Execute a command on the remote repository.
    /// </summary>
    /// <param name="method">Http method</param>
    /// <param name="accountName">Azure DevOps account name</param>
    /// <param name="projectName">Project name</param>
    /// <param name="requestPath">Path for request</param>
    /// <param name="logger">Logger</param>
    /// <param name="body">Optional body if <paramref name="method"/> is Put or Post</param>
    /// <param name="versionOverride">API version override</param>
    /// <param name="baseAddressSubpath">[baseAddressSubPath]dev.azure.com subdomain to make the request</param>
    /// <param name="retryCount">Maximum number of tries to attempt the API request</param>
    /// <returns>Http response</returns>
    public async Task<JObject> ExecuteAzureDevOpsAPIRequestAsync(
        HttpMethod method,
        string accountName,
        string projectName,
        string requestPath,
        ILogger logger,
        string body = null,
        string versionOverride = null,
        bool logFailure = true,
        string baseAddressSubpath = null,
        int retryCount = 15)
    {
        if (!AllowRetries)
        {
            retryCount = 0;
        }
        using (HttpClient client = CreateHttpClient(accountName, projectName, versionOverride, baseAddressSubpath))
        {
            var requestManager = new HttpRequestManager(client,
                method,
                requestPath,
                logger,
                body,
                versionOverride,
                logFailure);
            using (var response = await requestManager.ExecuteAsync(retryCount))
            {
                string responseContent = response.StatusCode == HttpStatusCode.NoContent ?
                    "{}" :
                    await response.Content.ReadAsStringAsync();

                return JObject.Parse(responseContent);
            }
        }
    }

    /// <summary>
    ///     Ensure that the input string ends with 'shouldEndWith' char. 
    ///     Returns null if input parameter is null.
    /// </summary>
    /// <param name="input">String that must have 'shouldEndWith' at the end.</param>
    /// <param name="shouldEndWith">Character that must be present at end of 'input' string.</param>
    /// <returns>Input string appended with 'shouldEndWith'</returns>
    private static string EnsureEndsWith(string input, char shouldEndWith)
    {
        if (input == null) return null;

        return input.TrimEnd(shouldEndWith) + shouldEndWith;
    }

    /// <summary>
    /// Create a new http client for talking to the specified azdo account name and project.
    /// </summary>
    /// <param name="versionOverride">Optional version override for the targeted API version.</param>
    /// <param name="baseAddressSubpath">Optional subdomain for the base address for the API. Should include the final dot.</param>
    /// <param name="accountName">Azure DevOps account</param>
    /// <param name="projectName">Azure DevOps project</param>
    /// <returns>New http client</returns>
    private HttpClient CreateHttpClient(string accountName, string projectName = null, string versionOverride = null, string baseAddressSubpath = null)
    {
        baseAddressSubpath = EnsureEndsWith(baseAddressSubpath, '.');

        string address = $"https://{baseAddressSubpath}dev.azure.com/{accountName}/";
        if (!string.IsNullOrEmpty(projectName))
        {
            address += $"{projectName}/";
        }

        var client = new HttpClient(new HttpClientHandler { CheckCertificateRevocationList = true })
        {
            BaseAddress = new Uri(address)
        };

        client.DefaultRequestHeaders.Add(
            "Accept",
            $"application/json;api-version={versionOverride ?? DefaultApiVersion}");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes(string.Format("{0}:{1}", "", _tokenProvider.GetTokenForAccount(accountName)))));

        return client;
    }

    /// <summary>
    /// Create a connection to AzureDevOps using the VSS APIs
    /// </summary>
    /// <param name="accountName">Uri of repository or pull request</param>
    /// <returns>New VssConnection</returns>
    private VssConnection CreateVssConnection(string accountName)
    {
        var accountUri = new Uri($"https://dev.azure.com/{accountName}");
        var creds = new VssCredentials(new VssBasicCredential("", _tokenProvider.GetTokenForAccount(accountName)));
        return new VssConnection(accountUri, creds);
    }

    /// <summary>
    /// Parse a repository url into its component parts.
    /// </summary>
    /// <param name="repoUri">Repository url to parse</param>
    /// <returns>Tuple of account, project, repo</returns>
    /// <remarks>
    ///     While we really only want to support dev.azure.com, in some cases
    ///     builds are still being reported from foo.visualstudio.com. This is apparently because
    ///     the url the agent connects to is what determines this property. So for now, support both forms.
    ///     We don't need to support this for PR urls because the repository target urls in the Maestro
    ///     database are restricted to dev.azure.com forms.
    /// </remarks>
    public static (string accountName, string projectName, string repoName) ParseRepoUri(string repoUri)
    {
        repoUri = NormalizeUrl(repoUri);

        Match m = RepositoryUriPattern.Match(repoUri);
        if (!m.Success)
        {
            m = LegacyRepositoryUriPattern.Match(repoUri);
            if (!m.Success)
            {
                throw new ArgumentException(
                    "Repository URI should be in the form https://dev.azure.com/:account/:project/_git/:repo or " +
                    "https://:account.visualstudio.com/:project/_git/:repo");
            }
        }

        return (m.Groups["account"].Value,
            m.Groups["project"].Value,
            m.Groups["repo"].Value);
    }

    /// <summary>
    ///   Returns the project ID for a combination of Azure DevOps account and project name
    /// </summary>
    /// <param name="accountName">Azure DevOps account</param>
    /// <param name="projectName">Azure DevOps project to get the ID for</param>
    /// <returns>Project Id</returns>
    public async Task<string> GetProjectIdAsync(string accountName, string projectName)
    {
        JObject content = await ExecuteAzureDevOpsAPIRequestAsync(
            HttpMethod.Get,
            accountName,
            "",
            $"_apis/projects/{projectName}",
            _logger,
            versionOverride: "5.0");
        return content["id"].ToString();
    }

    /// <summary>
    /// Parse a repository url into its component parts
    /// </summary>
    /// <param name="repoUri">Repository url to parse</param>
    /// <returns>Tuple of account, project, repo, and pr id</returns>
    public static (string accountName, string projectName, string repoName, int id) ParsePullRequestUri(string prUri)
    {
        Match m = PullRequestApiUriPattern.Match(prUri);
        if (!m.Success)
        {
            throw new ArgumentException(
                @"Pull request URI should be in the form  https://dev.azure.com/:account/:project/_apis/git/repositories/:repo/pullRequests/:id");
        }

        return (m.Groups["account"].Value,
            m.Groups["project"].Value,
            m.Groups["repo"].Value,
            int.Parse(m.Groups["id"].Value));
    }

    /// <summary>
    ///     Commit or update a set of files to a repo
    /// </summary>
    /// <param name="filesToCommit">Files to comit</param>
    /// <param name="repoUri">Remote repository URI</param>
    /// <param name="branch">Branch to push to</param>
    /// <param name="commitMessage">Commit message</param>
    /// <returns></returns>
    public async Task CommitFilesAsync(List<GitFile> filesToCommit, string repoUri, string branch, string commitMessage)
        => await CommitFilesAsync(
            filesToCommit,
            repoUri,
            branch,
            commitMessage,
            _logger,
            await _tokenProvider.GetTokenForRepositoryAsync(repoUri),
            "DotNet-Bot",
            "dn-bot@microsoft.com");

    /// <summary>
    ///   If the release pipeline doesn't have an artifact source a new one is added.
    ///   If the pipeline has a single artifact source the artifact definition is adjusted as needed.
    ///   If the pipeline has more than one source an error is thrown.
    ///     
    ///   The artifact source added (or the adjustment) has the following properties:
    ///     - Alias: PrimaryArtifact
    ///     - Type: Single Build
    ///     - Version: Specific
    /// </summary>
    /// <param name="accountName">Azure DevOps account name</param>
    /// <param name="projectName">Project name</param>
    /// <param name="releaseDefinition">Release definition to be updated</param>
    /// <param name="build">Build which should be added as source of the release definition.</param>
    /// <returns>AzureDevOpsReleaseDefinition</returns>
    public async Task<AzureDevOpsReleaseDefinition> AdjustReleasePipelineArtifactSourceAsync(string accountName, string projectName, AzureDevOpsReleaseDefinition releaseDefinition, AzureDevOpsBuild build)
    {
        if (releaseDefinition.Artifacts == null || releaseDefinition.Artifacts.Length == 0)
        {
            releaseDefinition.Artifacts = [
                new AzureDevOpsArtifact()
                {
                    Alias = "PrimaryArtifact",
                    Type = "Build",
                    DefinitionReference = new AzureDevOpsArtifactSourceReference()
                    {
                        Definition = new AzureDevOpsIdNamePair()
                        {
                            Id = build.Definition.Id,
                            Name = build.Definition.Name
                        },
                        DefaultVersionType = new AzureDevOpsIdNamePair()
                        {
                            Id = "specificVersionType",
                            Name = "Specific version"
                        },
                        DefaultVersionSpecific = new AzureDevOpsIdNamePair()
                        {
                            Id = build.Id.ToString(),
                            Name = build.BuildNumber
                        },
                        Project = new AzureDevOpsIdNamePair()
                        {
                            Id = build.Project.Id,
                            Name = build.Project.Name
                        }
                    }
                }
            ];
        }
        else if (releaseDefinition.Artifacts.Length == 1)
        {
            var definitionReference = releaseDefinition.Artifacts[0].DefinitionReference;

            definitionReference.Definition.Id = build.Definition.Id;
            definitionReference.Definition.Name = build.Definition.Name;

            definitionReference.DefaultVersionSpecific.Id = build.Id.ToString();
            definitionReference.DefaultVersionSpecific.Name = build.BuildNumber;

            definitionReference.Project.Id = build.Project.Id;
            definitionReference.Project.Name = build.Project.Name;

            if (!releaseDefinition.Artifacts[0].Alias.Equals("PrimaryArtifact"))
            {
                _logger.LogInformation($"The artifact source Alias for the release pipeline should be 'PrimaryArtifact' got '{releaseDefinition.Artifacts[0].Alias}'. Trying to patch it.");
                releaseDefinition.Artifacts[0].Alias = "PrimaryArtifact";
            }

            if (!releaseDefinition.Artifacts[0].Type.Equals("Build"))
            {
                _logger.LogInformation($"The artifact source Type for the release pipeline should be 'Build' got '{releaseDefinition.Artifacts[0].Type}'. Trying to patch it.");
                releaseDefinition.Artifacts[0].Type = "Build";
            }

            if (!definitionReference.DefaultVersionType.Id.Equals("specificVersionType"))
            {
                _logger.LogInformation($"The artifact source Id for the release pipeline should be 'specificVersionType' got '{definitionReference.DefaultVersionType.Id}'. Trying to patch it.");
                definitionReference.DefaultVersionType.Id = "specificVersionType";
            }

            if (!definitionReference.DefaultVersionType.Name.Equals("Specific version"))
            {
                _logger.LogInformation($"The artifact source Name for the release pipeline should be 'Specific version' got '{definitionReference.DefaultVersionType.Name}'. Trying to patch it.");
                definitionReference.DefaultVersionType.Name = "Specific version";
            }
        }
        else
        {
            throw new ArgumentException($"{releaseDefinition.Artifacts.Length} artifact sources are defined in pipeline {releaseDefinition.Id}. Only one artifact source was expected.");
        }

        var _serializerSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore
        };

        var body = JsonConvert.SerializeObject(releaseDefinition, _serializerSettings);

        JObject content = await ExecuteAzureDevOpsAPIRequestAsync(
            HttpMethod.Put,
            accountName,
            projectName,
            $"_apis/release/definitions/",
            _logger,
            body,
            versionOverride: "5.0",
            baseAddressSubpath: "vsrm.");

        return content.ToObject<AzureDevOpsReleaseDefinition>();
    }

    /// <summary>
    ///     Trigger a new release using the release definition informed. No change is performed
    ///     on the release definition - it is used as is.
    /// </summary>
    /// <param name="accountName">Azure DevOps account name</param>
    /// <param name="projectName">Project name</param>
    /// <param name="releaseDefinition">Release definition to be updated</param>
    /// <returns>Id of the started release</returns>
    public async Task<int> StartNewReleaseAsync(string accountName, string projectName, AzureDevOpsReleaseDefinition releaseDefinition, int barBuildId)
    {
        var body = $"{{ \"definitionId\": {releaseDefinition.Id}, \"variables\": {{ \"BARBuildId\": {{ \"value\": \"{barBuildId}\" }} }} }}";

        JObject content = await ExecuteAzureDevOpsAPIRequestAsync(
            HttpMethod.Post,
            accountName,
            projectName,
            $"_apis/release/releases/",
            _logger,
            body,
            versionOverride: "5.0",
            baseAddressSubpath: "vsrm.");

        return content.GetValue("id").ToObject<int>();
    }

    /// <summary>
    ///     Queue a new build on the specified build definition with the given queue time variables.
    /// </summary>
    /// <param name="accountName">Account where the project is hosted.</param>
    /// <param name="projectName">Project where the build definition is.</param>
    /// <param name="azdoDefinitionId">ID of the build definition where a build should be queued.</param>
    /// <param name="queueTimeVariables">Queue time variables as a Dictionary of (variable name, value).</param>
    /// <param name="templateParameters">Template parameters as a Dictionary of (variable name, value).</param>
    /// <param name="pipelineResources">Pipeline resources as a Dictionary of (pipeline resource name, build number).</param>
    public async Task<int> StartNewBuildAsync(
        string accountName,
        string projectName,
        int azdoDefinitionId,
        string sourceBranch,
        string sourceVersion,
        Dictionary<string, string> queueTimeVariables = null,
        Dictionary<string, string> templateParameters = null,
        Dictionary<string, string> pipelineResources = null)
    {
        var variables = queueTimeVariables?
            .ToDictionary(x => x.Key, x => new AzureDevOpsVariable(x.Value))
            ?? [];

        var pipelineResourceParameters = pipelineResources?
            .ToDictionary(x => x.Key, x => new AzureDevOpsPipelineResourceParameter(x.Value))
            ?? [];

        var repositoryBranch = sourceBranch.StartsWith(RefsHeadsPrefix) ? sourceBranch : RefsHeadsPrefix + sourceBranch;

        var body = new AzureDevOpsPipelineRunDefinition
        {
            Resources = new AzureDevOpsRunResourcesParameters
            {
                Repositories = new Dictionary<string, AzureDevOpsRepositoryResourceParameter>
                {
                    { "self", new AzureDevOpsRepositoryResourceParameter(repositoryBranch, sourceVersion) }
                },
                Pipelines = pipelineResourceParameters
            },
            TemplateParameters = templateParameters,
            Variables = variables
        };

        string bodyAsString = JsonConvert.SerializeObject(body, Formatting.Indented);

        JObject content = await ExecuteAzureDevOpsAPIRequestAsync(
            HttpMethod.Post,
            accountName,
            projectName,
            $"_apis/pipelines/{azdoDefinitionId}/runs",
            _logger,
            bodyAsString,
            versionOverride: "6.0-preview.1");

        return content.GetValue("id").ToObject<int>();
    }

    /// <summary>
    ///   Return the description of the release with ID informed.
    /// </summary>
    /// <param name="accountName">Azure DevOps account name</param>
    /// <param name="projectName">Project name</param>
    /// <param name="releaseId">ID of the release that should be looked up for</param>
    /// <returns>AzureDevOpsRelease</returns>
    public async Task<AzureDevOpsRelease> GetReleaseAsync(string accountName, string projectName, int releaseId)
    {
        JObject content = await ExecuteAzureDevOpsAPIRequestAsync(
            HttpMethod.Get,
            accountName,
            projectName,
            $"_apis/release/releases/{releaseId}",
            _logger,
            versionOverride: "5.1-preview.1",
            baseAddressSubpath: "vsrm.");

        return content.ToObject<AzureDevOpsRelease>();
    }

    /// <summary>
    ///   Gets all Artifact feeds in an Azure DevOps account.
    /// </summary>
    /// <param name="accountName">Azure DevOps account name</param>
    /// <returns>List of Azure DevOps feeds in the account</returns>
    public async Task<List<AzureDevOpsFeed>> GetFeedsAsync(string accountName)
    {
        JObject content = await ExecuteAzureDevOpsAPIRequestAsync(
            HttpMethod.Get,
            accountName,
            null,
            $"_apis/packaging/feeds",
            _logger,
            versionOverride: "5.1-preview.1",
            baseAddressSubpath: "feeds.");

        var list = ((JArray)content["value"]).ToObject<List<AzureDevOpsFeed>>();
        list.ForEach(f => f.Account = accountName);
        return list;
    }

    /// <summary>
    ///   Gets the list of Build Artifacts names.
    /// </summary>
    /// <param name="accountName">Azure DevOps account name</param>
    /// <param name="projectName">Project name</param>
    /// <returns>List of Azure DevOps build artifacts names.</returns>
    public async Task<List<AzureDevOpsBuildArtifact>> GetBuildArtifactsAsync(string accountName, string projectName, int azureDevOpsBuildId, int maxRetries = 15)
    {
        JObject content = await ExecuteAzureDevOpsAPIRequestAsync(
            HttpMethod.Get,
            accountName,
            projectName,
            $"_apis/build/builds/{azureDevOpsBuildId}/artifacts",
            _logger,
            versionOverride: "5.0",
            retryCount: maxRetries);

        return ((JArray)content["value"]).ToObject<List<AzureDevOpsBuildArtifact>>();
    }

    /// <summary>
    ///   Gets all Artifact feeds along with their packages in an Azure DevOps account.
    /// </summary>
    /// <param name="accountName">Azure DevOps account name.</param>
    /// <returns>List of Azure DevOps feeds in the account.</returns>
    public async Task<List<AzureDevOpsFeed>> GetFeedsAndPackagesAsync(string accountName)
    {
        var feeds = await GetFeedsAsync(accountName);
        feeds.ForEach(async feed => feed.Packages = await GetPackagesForFeedAsync(accountName, feed.Project?.Name, feed.Name));
        return feeds;
    }

    /// <summary>
    ///   Gets a specified Artifact feed in an Azure DevOps account.
    /// </summary>
    /// <param name="accountName">Azure DevOps account name</param>
    /// <param name="project">Azure DevOps project where the feed is hosted</param>
    /// <param name="feedIdentifier">ID or name of the feed</param>
    /// <returns>List of Azure DevOps feeds in the account</returns>
    public async Task<AzureDevOpsFeed> GetFeedAsync(string accountName, string project, string feedIdentifier)
    {
        JObject content = await ExecuteAzureDevOpsAPIRequestAsync(
            HttpMethod.Get,
            accountName,
            project,
            $"_apis/packaging/feeds/{feedIdentifier}",
            _logger,
            versionOverride: "5.1-preview.1",
            baseAddressSubpath: "feeds.");

        AzureDevOpsFeed feed = content.ToObject<AzureDevOpsFeed>();
        feed.Account = accountName;
        return feed;
    }

    /// <summary>
    ///   Gets a specified Artifact feed with their pacckages in an Azure DevOps account.
    /// </summary>
    /// <param name="accountName">Azure DevOps account name.</param>
    /// <param name="project">Azure DevOps project that the feed is hosted in</param>
    /// <param name="feedIdentifier">ID or name of the feed.</param>
    /// <returns>List of Azure DevOps feeds in the account.</returns>
    public async Task<AzureDevOpsFeed> GetFeedAndPackagesAsync(string accountName, string project, string feedIdentifier)
    {
        var feed = await GetFeedAsync(accountName, project, feedIdentifier);
        feed.Packages = await GetPackagesForFeedAsync(accountName, project, feedIdentifier);

        return feed;
    }

    /// <summary>
    /// Gets all packages in a given Azure DevOps feed
    /// </summary>
    /// <param name="accountName">Azure DevOps account name</param>
    /// <param name="project">Project that the feed was created in</param>
    /// <param name="feedIdentifier">Name or id of the feed</param>
    /// <param name="includeDeleted">Include deleted packages</param>
    /// <returns>List of packages in the feed</returns>
    public async Task<List<AzureDevOpsPackage>> GetPackagesForFeedAsync(string accountName, string project, string feedIdentifier, bool includeDeleted = true)
    {
        JObject content = await ExecuteAzureDevOpsAPIRequestAsync(
            HttpMethod.Get,
            accountName,
            project,
            $"_apis/packaging/feeds/{feedIdentifier}/packages?includeAllVersions=true" + (includeDeleted ? "&includeDeleted=true" : string.Empty),
            _logger,
            versionOverride: "5.1-preview.1",
            baseAddressSubpath: "feeds.");

        return ((JArray)content["value"]).ToObject<List<AzureDevOpsPackage>>();
    }

    /// <summary>
    ///   Deletes an Azure Artifacts feed and all of its packages
    /// </summary>
    /// <param name="accountName">Azure DevOps account name</param>
    /// <param name="project">Project that the feed was created in</param>
    /// <param name="feedIdentifier">Name or id of the feed</param>
    /// <returns>Async task</returns>
    public async Task DeleteFeedAsync(string accountName, string project, string feedIdentifier)
    {
        await ExecuteAzureDevOpsAPIRequestAsync(
            HttpMethod.Delete,
            accountName,
            project,
            $"_apis/packaging/feeds/{feedIdentifier}",
            _logger,
            versionOverride: "5.1-preview.1",
            baseAddressSubpath: "feeds.");
    }

    /// <summary>
    ///   Deletes a NuGet package version from a feed.
    /// </summary>
    /// <param name="accountName">Azure DevOps account name</param>
    /// <param name="project">Project that the feed was created in</param>
    /// <param name="feedIdentifier">Name or id of the feed</param>
    /// <param name="packageName">Name of the package</param>
    /// <param name="version">Version to delete</param>
    /// <returns>Async task</returns>
    public async Task DeleteNuGetPackageVersionFromFeedAsync(string accountName, string project, string feedIdentifier, string packageName, string version)
    {
        await ExecuteAzureDevOpsAPIRequestAsync(
            HttpMethod.Delete,
            accountName,
            project,
            $"_apis/packaging/feeds/{feedIdentifier}/nuget/packages/{packageName}/versions/{version}",
            _logger,
            versionOverride: "5.1-preview.1",
            baseAddressSubpath: "pkgs.");
    }

    /// <summary>
    ///   Fetches a list of last run AzDO builds for a given build definition.
    /// </summary>
    /// <param name="account">Azure DevOps account name</param>
    /// <param name="project">Project name</param>
    /// <param name="definitionId">Id of the pipeline (build definition)</param>
    /// <param name="branch">Filter by branch</param>
    /// <param name="count">Number of builds to retrieve</param>
    /// <param name="status">Filter by status</param>
    /// <returns>AzureDevOpsBuild</returns>
    public async Task<JObject> GetBuildsAsync(string account, string project, int definitionId, string branch, int count, string status)
        => await ExecuteAzureDevOpsAPIRequestAsync(
            HttpMethod.Get,
            account,
            project,
            $"_apis/build/builds?definitions={definitionId}&branchName={branch}&statusFilter={status}&$top={count}",
            _logger,
            versionOverride: "5.0");

    /// <summary>
    ///   Fetches an specific AzDO build based on its ID.
    /// </summary>
    /// <param name="accountName">Azure DevOps account name</param>
    /// <param name="projectName">Project name</param>
    /// <param name="buildId">Id of the build to be retrieved</param>
    /// <returns>AzureDevOpsBuild</returns>
    public async Task<AzureDevOpsBuild> GetBuildAsync(string accountName, string projectName, long buildId)
    {
        JObject content = await ExecuteAzureDevOpsAPIRequestAsync(
            HttpMethod.Get,
            accountName,
            projectName,
            $"_apis/build/builds/{buildId}",
            _logger,
            versionOverride: "5.0");

        return content.ToObject<AzureDevOpsBuild>();
    }

    /// <summary>
    ///     Fetches an specific AzDO release definition based on its ID.
    /// </summary>
    /// <param name="accountName">Azure DevOps account name</param>
    /// <param name="projectName">Project name</param>
    /// <param name="releaseDefinitionId">Id of the release definition to be retrieved</param>
    /// <returns>AzureDevOpsReleaseDefinition</returns>
    public async Task<AzureDevOpsReleaseDefinition> GetReleaseDefinitionAsync(string accountName, string projectName, long releaseDefinitionId)
    {
        JObject content = await ExecuteAzureDevOpsAPIRequestAsync(
            HttpMethod.Get,
            accountName,
            projectName,
            $"_apis/release/definitions/{releaseDefinitionId}",
            _logger,
            versionOverride: "5.0",
            baseAddressSubpath: "vsrm.");

        return content.ToObject<AzureDevOpsReleaseDefinition>();
    }

    /// <summary>
    /// If repoUri includes the user in the account we remove it from URIs like
    /// https://dnceng@dev.azure.com/dnceng/internal/_git/repo
    /// If the URL host is of the form "dnceng.visualstudio.com" like
    /// https://dnceng.visualstudio.com/internal/_git/repo we replace it to "dev.azure.com/dnceng"
    /// for consistency
    /// </summary>
    /// <param name="url">The original url</param>
    /// <returns>Transformed url</returns>
    public static string NormalizeUrl(string repoUri)
    {
        if (Uri.TryCreate(repoUri, UriKind.Absolute, out Uri parsedUri))
        {
            if (!string.IsNullOrEmpty(parsedUri.UserInfo))
            {
                repoUri = repoUri.Replace($"{parsedUri.UserInfo}@", string.Empty);
            }

            Match m = LegacyRepositoryUriPattern.Match(repoUri);

            if (m.Success)
            {
                string replacementUri = $"{Regex.Unescape(AzureDevOpsHostPattern)}/{m.Groups["account"].Value}";
                repoUri = repoUri.Replace(parsedUri.Host, replacementUri);
            }
        }

        return repoUri;
    }

    /// <summary>
    ///     Does not apply to remote repositories.
    /// </summary>
    /// <param name="commit">Ignored</param>
    public void Checkout(string repoPath, string commit, bool force)
    {
        throw new NotImplementedException($"Cannot checkout a remote repo.");
    }

    /// <summary>
    ///     Does not apply to remote repositories.
    /// </summary>
    /// <param name="repoDir">Ignored</param>
    /// <param name="repoUrl">Ignored</param>
    public string AddRemoteIfMissing(string repoDir, string repoUrl)
    {
        throw new NotImplementedException("Cannot add a remote to a remote repo.");
    }

    /// <summary>
    /// Checks that a repository exists
    /// </summary>
    /// <param name="repoUri">Repository uri</param>
    /// <returns>True if the repository exists, false otherwise.</returns>
    public async Task<bool> RepoExistsAsync(string repoUri)
    {
        (string accountName, string projectName, string repoName) = ParseRepoUri(repoUri);

        try
        {
            await ExecuteAzureDevOpsAPIRequestAsync(
                HttpMethod.Get,
                accountName,
                projectName,
                $"_apis/git/repositories/{repoName}",
                _logger,
                logFailure: false);
            return true;
        }
        catch (Exception) { }

        return false;
    }

    /// <summary>
    /// Deletes the head branch for a pull request
    /// </summary>
    /// <param name="pullRequestUri">Pull request Uri</param>
    /// <returns>Async task</returns>
    public async Task DeletePullRequestBranchAsync(string pullRequestUri)
    {
        PullRequest pr = await GetPullRequestAsync(pullRequestUri);
        (string account, string project, string repo, int id) prInfo = ParsePullRequestUri(pullRequestUri);
        await DeleteBranchAsync(prInfo.account, prInfo.project, prInfo.repo, pr.HeadBranch);
    }

    /// <summary>
    /// Helper function for truncating strings to a set length.
    ///  See https://github.com/dotnet/arcade/issues/5811 
    /// </summary>
    /// <param name="str">String to be shortened if necessary</param>
    /// <returns></returns>
    private static string TruncateDescriptionIfNeeded(string str)
    {
        if (str.Length > MaxPullRequestDescriptionLength)
        {
            return str.Substring(0, MaxPullRequestDescriptionLength);
        }
        return str;
    }

    public async Task CommentPullRequestAsync(string pullRequestUri, string comment)
    {
        (string accountName, string _, string repoName, int id) = ParsePullRequestUri(pullRequestUri);

        using VssConnection connection = CreateVssConnection(accountName);
        using GitHttpClient client = await connection.GetClientAsync<GitHttpClient>();

        var prComment = new Comment()
        {
            CommentType = CommentType.Text,
            Content = $"{comment}{CommentMarker}"
        };

        var newCommentThread = new GitPullRequestCommentThread()
        {
            Comments = [prComment]
        };
        await client.CreateThreadAsync(newCommentThread, repoName, id);
    }

    public async Task<List<GitTreeItem>> LsTreeAsync(string uri, string gitRef, string path = null)
    {
        _logger.LogInformation($"Getting tree contents from repo '{uri}', ref '{gitRef}', path '{path}'");
        
        (string accountName, string projectName, string repoName) = ParseRepoUri(uri);
        
        // First, we need to get the commit object to find the tree SHA
        string treeSha;

        if (_gitRefCommitCache.TryGetValue((uri, gitRef, path), out var cachedSha))
        {
            treeSha = cachedSha;
        }
        else
        {
            string commitSha = await GetCommitShaForGitRefAsync(accountName, projectName, repoName, gitRef);

            // Get the commit to find the tree SHA
            JObject commitResponse = await ExecuteAzureDevOpsAPIRequestAsync(
                HttpMethod.Get,
                accountName,
                projectName,
                $"_apis/git/repositories/{repoName}/commits/{commitSha}",
                _logger);

            // Get the tree SHA from the commit
            treeSha = commitResponse["treeId"].ToString();

            // If path is specified, we need to navigate to that tree
            if (!string.IsNullOrEmpty(path))
            {
                treeSha = await GetTreeShaForPathAsync(accountName, projectName, repoName, treeSha, path);
            }
        }

        // Now get the contents of the tree
        JObject treeResponse = await ExecuteAzureDevOpsAPIRequestAsync(
            HttpMethod.Get,
            accountName,
            projectName,
            $"_apis/git/repositories/{repoName}/trees/{treeSha}?recursive=false",
            _logger);

        // Map the tree entries to the expected return format
        var entries = treeResponse["treeEntries"].ToObject<JArray>();
        List<GitTreeItem> result = [];

        foreach (var entry in entries)
        {
            var objectType = entry["gitObjectType"].ToString().ToLowerInvariant();
            var sha = entry["objectId"].ToString();
            var treePath = $"{path}/{entry["relativePath"].ToString()}";

            if (objectType == "tree")
            {
                _gitRefCommitCache[(uri, gitRef, treePath)] = sha;
            }

            result.Add(new GitTreeItem {
                Sha = sha,
                Path = treePath,
                Type = objectType
            });
        }

        return result;
    }

    /// <summary>
    /// Navigate to a specific path within a tree to find its SHA
    /// </summary>
    private async Task<string> GetTreeShaForPathAsync(
        string accountName, 
        string projectName, 
        string repoName, 
        string treeSha, 
        string path)
    {
        var pathSegments = path.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        var currentTreeSha = treeSha;

        foreach (var segment in pathSegments)
        {
            // Get the current tree
            JObject treeResponse = await ExecuteAzureDevOpsAPIRequestAsync(
                HttpMethod.Get,
                accountName,
                projectName,
                $"_apis/git/repositories/{repoName}/trees/{currentTreeSha}?recursive=false",
                _logger);

            // Find the entry matching the current path segment
            var entries = treeResponse["treeEntries"].ToObject<JArray>();
            var matchingEntry = entries.FirstOrDefault(e => 
                e["relativePath"].ToString() == segment && 
                e["gitObjectType"].ToString().ToLowerInvariant() == "tree");

            if (matchingEntry == null)
            {
                throw new DirectoryNotFoundException($"Path segment '{segment}' not found in tree.");
            }

            currentTreeSha = matchingEntry["objectId"].ToString();
        }

        return currentTreeSha;
    }

    private async Task<string> GetCommitShaForGitRefAsync(
        string accountName,
        string projectName,
        string repoName,
        string gitRef)
    {
        string commitSha;

        // Try to resolve the reference as a branch, commit, or tag
        try
        {
            // Try as a branch first (most common case)
            commitSha = await GetCommitShaFromBranchOrTagRefAsync(accountName, projectName, repoName, $"heads/{gitRef}");
        }
        catch
        {
            try
            {
                // Try as a tag
                commitSha = await GetCommitShaFromBranchOrTagRefAsync(accountName, projectName, repoName, $"tags/{gitRef}");
            }
            catch
            {
                try
                {
                    commitSha = await GetCommitShaDirectAsync(accountName, projectName, repoName, gitRef);
                }
                catch (Exception ex)
                {
                    throw new ArgumentException($"Could not resolve '{gitRef}' as a branch, tag, or commit in repository '{repoName}'", ex);
                }
            }
        }

        return commitSha;
    }

    /// <summary>
    /// Get a commit SHA from a branch reference
    /// </summary>
    private async Task<string> GetCommitShaFromBranchOrTagRefAsync(
        string accountName,
        string projectName,
        string repoName,
        string gitRef)
    {        
        // Get the ref
        JObject refResponse = await ExecuteAzureDevOpsAPIRequestAsync(
            HttpMethod.Get,
            accountName,
            projectName,
            $"_apis/git/repositories/{repoName}/refs?filter={Uri.EscapeDataString(gitRef)}",
            _logger);

        // Extract the commit SHA from the ref
        var refs = refResponse["value"].ToObject<JArray>();
        if (refs.Count == 0)
        {
            throw new DarcException($"Branch '{gitRef}' not found in repository '{repoName}'");
        }
        
        return refs[0]["objectId"].ToString();
    }

    /// <summary>
    /// Get a commit SHA directly (for when the reference is itself a commit SHA or commit-ish)
    /// </summary>
    private async Task<string> GetCommitShaDirectAsync(
        string accountName,
        string projectName,
        string repoName,
        string commitSha)
    {
        try
        {
            // Try to get the commit directly
            JObject commitResponse = await ExecuteAzureDevOpsAPIRequestAsync(
                HttpMethod.Get,
                accountName,
                projectName,
                $"_apis/git/repositories/{repoName}/commits/{commitSha}",
                _logger);

            return commitResponse["commitId"].ToString();
        }
        catch (Exception ex)
        {
            throw new DarcException($"Failed to find commit '{commitSha}' in repository '{repoName}'", ex);
        }
    }

    public async Task<List<string>> GetPullRequestCommentsAsync(string pullRequestUrl)
    {
        (string accountName, string projectName, string repoName, int id) = ParsePullRequestUri(pullRequestUrl);

        _logger.LogInformation("Retrieving comments for pull request {PullRequestUrl}", pullRequestUrl);

        using VssConnection connection = CreateVssConnection(accountName);
        using GitHttpClient client = await connection.GetClientAsync<GitHttpClient>();

        List<GitPullRequestCommentThread> commentThreads = await client.GetThreadsAsync(repoName, id);
        var comments = new List<string>();

        foreach (GitPullRequestCommentThread commentThread in commentThreads)
        {
            // Only get comments from active and unknown threads (active threads may appear as unknown)
            if (commentThread.Status == CommentThreadStatus.Active || 
                commentThread.Status == CommentThreadStatus.Unknown)
            {
                List<Comment> threadComments = await client.GetCommentsAsync(repoName, id, commentThread.Id);
                
                foreach (Comment comment in threadComments)
                {
                    if (comment.CommentType == CommentType.Text && !string.IsNullOrEmpty(comment.Content))
                    {
                        comments.Add(comment.Content);
                    }
                }
            }
        }

        return comments;
    }
}
