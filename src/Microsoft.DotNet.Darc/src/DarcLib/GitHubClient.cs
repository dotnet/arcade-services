// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Octokit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.DotNet.Services.Utility;
using Maestro.Contracts;

namespace Microsoft.DotNet.DarcLib
{
    public class GitHubClient : RemoteRepoBase, IGitRepo
    {
        private const string GitHubApiUri = "https://api.github.com";
        private const string DarcLibVersion = "1.0.0";
        private static readonly ProductHeaderValue _product;

        private static readonly string CommentMarker =
            "\n\n[//]: # (This identifies this comment as a Maestro++ comment)\n";

        private static readonly Regex RepositoryUriPattern = new Regex(@"^/(?<owner>[^/]+)/(?<repo>[^/]+)/?$");

        private static readonly Regex PullRequestUriPattern =
            new Regex(@"^/repos/(?<owner>[^/]+)/(?<repo>[^/]+)/pulls/(?<id>\d+)$");

        private readonly Lazy<Octokit.IGitHubClient> _lazyClient;
        private readonly ILogger _logger;
        private readonly string _personalAccessToken;
        private readonly JsonSerializerSettings _serializerSettings;
        private readonly string _userAgent = $"DarcLib-{DarcLibVersion}";

        static GitHubClient()
        {
            string version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                .InformationalVersion;
            _product = new ProductHeaderValue("DarcLib", version);
        }

        public GitHubClient(string gitExecutable, string accessToken, ILogger logger, string temporaryRepositoryPath, IMemoryCache cache)
            : base(gitExecutable, temporaryRepositoryPath, cache)
        {
            _personalAccessToken = accessToken;
            _logger = logger;
            _serializerSettings = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                NullValueHandling = NullValueHandling.Ignore
            };
            _lazyClient = new Lazy<Octokit.IGitHubClient>(CreateGitHubClientClient);
        }

        public virtual Octokit.IGitHubClient Client => _lazyClient.Value;

        public bool AllowRetries { get; set; } = true;

        /// <summary>
        ///     Retrieve the contents of a repository file as a string
        /// </summary>
        /// <param name="filePath">Path to file</param>
        /// <param name="repoUri">Repository URI</param>
        /// <param name="branch">Branch to get file contents from</param>
        /// <returns>File contents or throws on file not found.</returns>
        public async Task<string> GetFileContentsAsync(string filePath, string repoUri, string branch)
        {
            (string owner, string repo) = ParseRepoUri(repoUri);
            return await GetFileContentsAsync(owner, repo, filePath, branch);
        }

        /// <summary>
        ///     Retrieve the contents of a repository file as a string
        /// </summary>
        /// <param name="filePath">Path to file</param>
        /// <param name="owner">Owner of repo</param>
        /// <param name="repo">Repo name</param>
        /// <param name="branch">Branch to get file contents from</param>
        /// <returns>File contents or throws on file not found.</returns>
        private async Task<string> GetFileContentsAsync(string owner, string repo, string filePath, string branch)
        {
            _logger.LogInformation(
                $"Getting the contents of file '{filePath}' from repo '{owner}/{repo}' in branch '{branch}'...");

            JObject responseContent;
            try
            {
                using (HttpResponseMessage response = await this.ExecuteRemoteGitCommandAsync(
                    HttpMethod.Get,
                    $"repos/{owner}/{repo}/contents/{filePath}?ref={branch}",
                    _logger,
                    logFailure: false))
                {
                    responseContent = JObject.Parse(await response.Content.ReadAsStringAsync());
                }

                string content = responseContent["content"].ToString();

                _logger.LogInformation(
                    $"Getting the contents of file '{filePath}' from repo '{owner}/{repo}' in branch '{branch}' succeeded!");

                return this.GetDecodedContent(content);
            }
            catch (HttpRequestException reqEx) when (reqEx.Message.Contains("404 (Not Found)"))
            {
                throw new DependencyFileNotFoundException(filePath, $"{owner}/{repo}", branch, reqEx);
            }
        }

        /// <summary>
        /// Create a new branch in a repository
        /// </summary>
        /// <param name="repoUri">Repo to create a branch in</param>
        /// <param name="newBranch">New branch name</param>
        /// <param name="baseBranch">Base of new branch</param>
        /// <returns></returns>
        public async Task CreateBranchAsync(string repoUri, string newBranch, string baseBranch)
        {
            _logger.LogInformation(
                $"Verifying if '{newBranch}' branch exist in repo '{repoUri}'. If not, we'll create it...");

            (string owner, string repo) = ParseRepoUri(repoUri);
            string latestSha = await GetLastCommitShaAsync(owner, repo, baseBranch);
            string body;

            string gitRef = $"refs/heads/{newBranch}";
            var githubRef = new GitHubRef(gitRef, latestSha);
            try
            {
                // If this succeeds, then the branch exists and we should
                // update the branch to latest.

                using (await this.ExecuteRemoteGitCommandAsync(
                    HttpMethod.Get,
                    $"repos/{owner}/{repo}/branches/{newBranch}",
                    _logger,
                    retryCount: 0)) { }

                githubRef.Force = true;
                body = JsonConvert.SerializeObject(githubRef, _serializerSettings);
                using (await this.ExecuteRemoteGitCommandAsync(
                    new HttpMethod("PATCH"),
                    $"repos/{owner}/{repo}/git/{gitRef}",
                    _logger,
                    body)) { }

                _logger.LogInformation($"Branch '{newBranch}' exists, updated");
            }
            catch (HttpRequestException exc) when (exc.Message.Contains(((int) HttpStatusCode.NotFound).ToString()))
            {
                _logger.LogInformation($"'{newBranch}' branch doesn't exist. Creating it...");

                body = JsonConvert.SerializeObject(githubRef, _serializerSettings);
                using (await this.ExecuteRemoteGitCommandAsync(
                    HttpMethod.Post,
                    $"repos/{owner}/{repo}/git/refs",
                    _logger,
                    body)) { }

                    _logger.LogInformation($"Branch '{newBranch}' created in repo '{repoUri}'!");

                return;
            }
            catch (HttpRequestException exc)
            {
                _logger.LogError(
                    $"Checking if '{newBranch}' branch existed in repo '{repoUri}' failed with '{exc.Message}'");
                throw;
            }
        }

        /// <summary>
        /// Deletes a branch in a repository
        /// </summary>
        /// <param name="repoUri">Repository URL</param>
        /// <param name="branch">Branch to delete</param>
        /// <returns>Async Task</returns>
        public async Task DeleteBranchAsync(string repoUri, string branch)
        {
            (string owner, string repo) = ParseRepoUri(repoUri);

            await DeleteBranchAsync(owner, repo, branch);
        }

        /// <summary>
        /// Deletes a branch in a repository
        /// </summary>
        /// <param name="owner">Repository owner</param>
        /// <param name="repo">Repository name</param>
        /// <param name="branch">Branch to delete</param>
        /// <returns></returns>
        private async Task DeleteBranchAsync(string owner, string repo, string branch)
        {
            await Client.Git.Reference.Delete(owner, repo, $"heads/{branch}");
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
            (string owner, string repo) = ParseRepoUri(repoUri);
            var query = new StringBuilder();

            if (!string.IsNullOrEmpty(keyword))
            {
                query.Append(keyword);
                query.Append("+");
            }

            query.Append($"repo:{owner}/{repo}+head:{pullRequestBranch}+type:pr+is:{status.ToString().ToLower()}");

            if (!string.IsNullOrEmpty(author))
            {
                query.Append($"+author:{author}");
            }

            JObject responseContent;
            using (HttpResponseMessage response = await this.ExecuteRemoteGitCommandAsync(
                HttpMethod.Get,
                $"search/issues?q={query}",
                _logger))
            {
                responseContent = JObject.Parse(await response.Content.ReadAsStringAsync());
            }

            JArray items = JArray.Parse(responseContent["items"].ToString());

            IEnumerable<int> prs = items.Select(r => r["number"].ToObject<int>());

            return prs;
        }

        /// <summary>
        /// Get the status of a pull request
        /// </summary>
        /// <param name="pullRequestUrl">URI of pull request</param>
        /// <returns>Pull request status</returns>
        public async Task<PrStatus> GetPullRequestStatusAsync(string pullRequestUrl)
        {
            (string owner, string repo, int id) = ParsePullRequestUri(pullRequestUrl);

            JObject responseContent;
            using (HttpResponseMessage response = await this.ExecuteRemoteGitCommandAsync(HttpMethod.Get,
                $"repos/{owner}/{repo}/pulls/{id}", _logger))
            {
                responseContent = JObject.Parse(await response.Content.ReadAsStringAsync());
            }

            if (Enum.TryParse(responseContent["state"].ToString(), true, out PrStatus status))
            {
                if (status == PrStatus.Open)
                {
                    return status;
                }

                if (status == PrStatus.Closed)
                {
                    if (bool.TryParse(responseContent["merged"].ToString(), out bool merged))
                    {
                        if (merged)
                        {
                            return PrStatus.Merged;
                        }
                    }

                    return PrStatus.Closed;
                }
            }

            return PrStatus.None;
        }

        /// <summary>
        ///     Retrieve information on a specific pull request
        /// </summary>
        /// <param name="pullRequestUrl">Uri of the pull request</param>
        /// <returns>Information on the pull request.</returns>
        public async Task<PullRequest> GetPullRequestAsync(string pullRequestUrl)
        {
            (string owner, string repo, int id) = ParsePullRequestUri(pullRequestUrl);
            Octokit.PullRequest pr = await Client.PullRequest.Get(owner, repo, id);
            return new PullRequest
            {
                Title = pr.Title,
                Description = pr.Body,
                BaseBranch = pr.Base.Ref,
                HeadBranch = pr.Head.Ref
            };
        }

        /// <summary>
        ///     Create a new pull request for a repository
        /// </summary>
        /// <param name="repoUri">Repo to create the pull request for.</param>
        /// <param name="pullRequest">Pull request data</param>
        /// <returns></returns>
        public async Task<string> CreatePullRequestAsync(string repoUri, PullRequest pullRequest)
        {
            (string owner, string repo) = ParseRepoUri(repoUri);

            var pr = new NewPullRequest(pullRequest.Title, pullRequest.HeadBranch, pullRequest.BaseBranch)
            {
                Body = pullRequest.Description
            };
            Octokit.PullRequest createdPullRequest = await Client.PullRequest.Create(owner, repo, pr);

            return createdPullRequest.Url;
        }

        /// <summary>
        ///     Update a pull request with new information
        /// </summary>
        /// <param name="pullRequestUri">Uri of pull request to update</param>
        /// <param name="pullRequest">Pull request info to update</param>
        /// <returns></returns>
        public async Task UpdatePullRequestAsync(string pullRequestUri, PullRequest pullRequest)
        {
            (string owner, string repo, int id) = ParsePullRequestUri(pullRequestUri);

            await Client.PullRequest.Update(
                owner,
                repo,
                id,
                new PullRequestUpdate
                {
                    Title = pullRequest.Title,
                    Body = pullRequest.Description
                });
        }

        /// <summary>
        /// Gets all the commits related to the pull request
        /// </summary>
        /// <param name="pullRequestUrl"></param>
        /// <returns>All the commits related to the pull request</returns>
        public async Task<IList<Commit>> GetPullRequestCommitsAsync(string pullRequestUrl)
        {
            (string owner, string repo, int id) = ParsePullRequestUri(pullRequestUrl);
            var pullRequestCommits = await Client.PullRequest.Commits(owner, repo, id);
            IList<Commit> commits = new List<Commit>(pullRequestCommits.Count);
            foreach (var commit in pullRequestCommits)
            {
                commits.Add(new Commit(commit.Commit.Author.Name,
                    commit.Sha,
                    commit.Commit.Message));
            }
            return commits;
        }

        /// <summary>
        ///     Merge a dependency update pull request
        /// </summary>
        /// <param name="pullRequestUrl">Uri of pull request to merge</param>
        /// <param name="parameters">Settings for merge</param>
        /// <param name="mergeCommitMessage">Commit message used to merge the pull request</param>
        /// <returns></returns>
        public async Task MergeDependencyPullRequestAsync(string pullRequestUrl, MergePullRequestParameters parameters, string mergeCommitMessage)
        {
            (string owner, string repo, int id) = ParsePullRequestUri(pullRequestUrl);
            Octokit.PullRequest pr = await Client.PullRequest.Get(owner, repo, id);
            
            var mergePullRequest = new MergePullRequest
            {
                CommitMessage = mergeCommitMessage,
                Sha = parameters.CommitToMerge,
                MergeMethod = parameters.SquashMerge ? PullRequestMergeMethod.Squash : PullRequestMergeMethod.Merge
            };

            await Client.PullRequest.Merge(owner, repo, id, mergePullRequest);

            if (parameters.DeleteSourceBranch)
            {
                await Client.Git.Reference.Delete(owner, repo, $"heads/{pr.Head.Ref}");
            }
        }

        /// <summary>
        ///     Create a new comment, or update the last comment with an updated message,
        ///     if that comment was created by Darc.
        /// </summary>
        /// <param name="pullRequestUrl">Url of pull request</param>
        /// <param name="message">Message to post</param>
        public async Task CreateOrUpdatePullRequestCommentAsync(string pullRequestUrl, string message)
        {
            (string owner, string repo, int id) = ParsePullRequestUri(pullRequestUrl);
            IssueComment lastComment = (await Client.Issue.Comment.GetAllForIssue(owner, repo, id)).LastOrDefault();
            if (lastComment != null && lastComment.Body.EndsWith(CommentMarker))
            {
                await Client.Issue.Comment.Update(owner, repo, lastComment.Id, message + CommentMarker);
            }
            else
            {
                await Client.Issue.Comment.Create(owner, repo, id, message + CommentMarker);
            }
        }

        public async Task CreateOrUpdatePullRequestMergeStatusInfoAsync(string pullRequestUrl, IReadOnlyList<MergePolicyEvaluationResult.SingleResult> evaluations)
        {
            (string owner, string repo, int id) = ParsePullRequestUri(pullRequestUrl);

            // Get the latest commit for the current PR
            IList<Commit> commits = await GetPullRequestCommitsAsync(pullRequestUrl);
            Commit latestCommit = commits[commits.Count - 1];

            // Get all the checks runs for the current PR
            CheckRunsResponse existingChecksRuns = await Client.Check.Run.GetAllForReference(owner,repo, latestCommit.Sha);
            // Convert the IReadOnlyList of CheckRun to a List of CheckRun
            List<CheckRun> existingChecksList = new List<CheckRun>(existingChecksRuns.CheckRuns);

            foreach (var eval in evaluations)
            {
                CheckRun existingCheckRun = existingChecksRuns.CheckRuns.SingleOrDefault(c => c.ExternalId == $"maestro-policy-{eval.MergePolicyName}-{latestCommit.Sha}");
                NewCheckRun newCheckRun = CreateNewCheckRun(eval, latestCommit.Sha);
                // If the check doesn't exist yet, create it
                if (existingCheckRun == null)
                {
                    await Client.Check.Run.Create(owner,repo, newCheckRun);
                }

                // If the check exist, checks that the status are different to update it
                else if (existingCheckRun != null && newCheckRun.Status != existingCheckRun.Status)
                {
                    CheckRunUpdate updatedCheck = new CheckRunUpdate();
                    updatedCheck.Status = newCheckRun.Status;
                    updatedCheck.Conclusion = newCheckRun.Conclusion;
                    updatedCheck.Name = newCheckRun.Name;
                    updatedCheck.CompletedAt = newCheckRun.CompletedAt;
                    existingChecksList.Remove(existingCheckRun);
                    await Client.Check.Run.Update(owner,repo, existingCheckRun.Id, updatedCheck);
                }
            }

            // Remove the check(s) in existingChecksRuns that aren't in evaluations 
            foreach (var remainingCheck in existingChecksList)
            {
                MergePolicyEvaluationResult.SingleResult remainingCheckCommon = evaluations.SingleOrDefault(eval => remainingCheck.ExternalId == $"maestro-policy-{eval.MergePolicyName}-{latestCommit.Sha}");
                // Avoid deleting check(s) that aren't from maestro
                if (remainingCheckCommon != null)
                {
                    CheckRunUpdate updatedCheck = new CheckRunUpdate();
                    updatedCheck.Status = "completed";
                    updatedCheck.Conclusion = "skipped";
                    updatedCheck.Name = remainingCheck.Name;
                    updatedCheck.CompletedAt = remainingCheck.CompletedAt;
                    await Client.Check.Run.Update(owner, repo, remainingCheck.Id, updatedCheck);
                }
            }
        }

        private NewCheckRun CreateNewCheckRun(MergePolicyEvaluationResult.SingleResult result, string sha)
        {

            // TODO: Investigate this condition, in which situation can this happen (and what we should do if that happens) ?
            // Initially we just returned a faile status with the result message
            if (result.MergePolicyName == null)
            {
                var newCheck = new NewCheckRun($"{result.MergePolicyDisplayName}", sha);
                var output = new NewCheckRunOutput(result.MergePolicyName, result.Message);
                newCheck.Output = output;
                newCheck.ExternalId = $"maestro-policy-{result.MergePolicyName}-{sha}";
                newCheck.Status = CheckStatus.Completed;
                newCheck.Conclusion = "failure";
                newCheck.CompletedAt = DateTime.Now;
                return newCheck;
            }

            if (result.Success == null)
            {
                var newCheck = new NewCheckRun($"{result.MergePolicyDisplayName}", sha);
                var output = new NewCheckRunOutput(result.MergePolicyName, result.Message);
                newCheck.Output = output;
                newCheck.ExternalId = $"maestro-policy-{result.MergePolicyName}-{sha}";
                newCheck.Status = CheckStatus.InProgress;
                return newCheck;

            }

            if (result.Success == true)
            {
                var newCheck = new NewCheckRun($"{result.MergePolicyDisplayName}", sha);
                var output = new NewCheckRunOutput(result.MergePolicyName, result.Message == null ? "" : $" - {result.Message}");
                newCheck.Output = output;
                newCheck.ExternalId = $"maestro-policy-{result.MergePolicyName}-{sha}";
                newCheck.Status = CheckStatus.Completed;
                newCheck.Conclusion = "success";
                newCheck.CompletedAt = DateTime.Now;
                return newCheck;

            }
            var newCheckRun = new NewCheckRun($"{result.MergePolicyDisplayName} {result.Message}", sha);
            var newCheckRunOutput = new NewCheckRunOutput(result.MergePolicyName, result.Message);
            newCheckRun.Output = newCheckRunOutput;
            newCheckRun.ExternalId = $"maestro-policy-{result.MergePolicyName}-{sha}";
            newCheckRun.Status = CheckStatus.Completed;
            newCheckRun.Conclusion = "success";
            newCheckRun.CompletedAt = DateTime.Now;
            return newCheckRun;

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
            path = path.Replace('\\', '/');
            path = path.TrimStart('/').TrimEnd('/');

            (string owner, string repo) = ParseRepoUri(repoUri);

            if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(repo))
            {
                _logger.LogInformation($"'owner' or 'repository' couldn't be inferred from '{repoUri}'. " +
                    $"Not getting files from 'eng/common...'");
                return new List<GitFile>();
            }

            TreeResponse pathTree = await GetTreeForPathAsync(owner, repo, commit, path);

            TreeResponse recursiveTree = await GetRecursiveTreeAsync(owner, repo, pathTree.Sha);

            GitFile[] files = await Task.WhenAll(
                recursiveTree.Tree.Where(treeItem => treeItem.Type == TreeType.Blob)
                    .Select(
                        async treeItem =>
                        {
                            return await GetGitTreeItem(path, treeItem, owner, repo);
                        }));
            return files.ToList();
        }

        /// <summary>
        ///     Get a tree item blob from github, using the cache if it exists.
        /// </summary>
        /// <param name="path">Base path of final git file</param>
        /// <param name="treeItem">Tree item to retrieve</param>
        /// <param name="owner">Organization</param>
        /// <param name="repo">Repository</param>
        /// <returns>Git file with tree item contents.</returns>
        public async Task<GitFile> GetGitTreeItem(string path, TreeItem treeItem, string owner, string repo)
        {
            // If we have a cache available here, attempt to get the value in the cache
            // before making the request. Generally, we are requesting the same files for each
            // arcade update sent to each repository daily per subscription. This is inefficient, as it means we
            // request each file N times, where N is the number of subscriptions. The number of files (M),
            // is non-trivial, so reducing N*M to M is vast improvement.
            // Use a combination of (treeItem.Path, treeItem.Sha as the key) as items with identical contents but
            // different paths will have the same SHA. I think it is overkill to hash the repo and owner into
            // the key.

            if (Cache != null)
            {
                return await Cache.GetOrCreateAsync((treeItem.Path, treeItem.Sha), async (entry) =>
                {
                    GitFile file = await GetGitItemImpl(path, treeItem, owner, repo);

                    // Set the size of the entry. The size is not computed by the caching system
                    // (it has no way to do so). There are two bytes per each character in a string.
                    // We do not really need to worry about the size of the GitFile class itself,
                    // just the variable length elements.
                    entry.Size = 2 * (file.Content.Length + file.FilePath.Length + file.Mode.Length);

                    return file;
                });
            }
            else
            {
                return await GetGitItemImpl(path, treeItem, owner, repo);
            }
        }

        /// <summary>
        ///     Get a tree item blob from github.
        /// </summary>
        /// <param name="path">Base path of final git file</param>
        /// <param name="treeItem">Tree item to retrieve</param>
        /// <param name="owner">Organization</param>
        /// <param name="repo">Repository</param>
        /// <returns>Git file with tree item contents.</returns>
        private async Task<GitFile> GetGitItemImpl(string path, TreeItem treeItem, string owner, string repo)
        {
            Octokit.Blob blob = await ExponentialRetry.RetryAsync(
                                async () => await Client.Git.Blob.Get(owner, repo, treeItem.Sha),
                                ex => _logger.LogError(ex, $"Failed to get blob at sha {treeItem.Sha}"),
                                ex => ex is ApiException apiex && apiex.StatusCode >= HttpStatusCode.InternalServerError);

            ContentEncoding encoding;
            switch (blob.Encoding.Value)
            {
                case EncodingType.Base64:
                    encoding = ContentEncoding.Base64;
                    break;
                case EncodingType.Utf8:
                    encoding = ContentEncoding.Utf8;
                    break;
                default:
                    throw new NotImplementedException($"Unknown github encoding type {blob.Encoding.StringValue}");

            }
            GitFile newFile = new GitFile(
                path + "/" + treeItem.Path,
                blob.Content,
                encoding,
                treeItem.Mode);

            return newFile;
        }

        /// <summary>
        /// Execute a remote github command using the REST APi
        /// </summary>
        /// <param name="method">Http method</param>
        /// <param name="requestUri">Request path</param>
        /// <param name="logger">Logger</param>
        /// <param name="body">Body if <paramref name="method"/> is POST or PATCH</param>
        /// <param name="versionOverride">Use alternate API version, if specified.</param>
        /// <returns></returns>
        private async Task<HttpResponseMessage> ExecuteRemoteGitCommandAsync(
            HttpMethod method,
            string requestUri,
            ILogger logger,
            string body = null,
            string versionOverride = null,
            int retryCount = 15,
            bool logFailure = true)
        {
            if (!AllowRetries)
            {
                retryCount = 0;
            }
            using (HttpClient client = CreateHttpClient())
            {
                var requestManager = new HttpRequestManager(client, method, requestUri, logger, body, versionOverride, logFailure);
                return await requestManager.ExecuteAsync(retryCount);
            }
        }

        /// <summary>
        /// Create a new http client for talking to github.
        /// </summary>
        /// <returns>New http client</returns
        private HttpClient CreateHttpClient()
        {
            var client = new HttpClient(new HttpClientHandler { CheckCertificateRevocationList = true }) {BaseAddress = new Uri(GitHubApiUri)};
            client.DefaultRequestHeaders.Add("Authorization", $"Token {_personalAccessToken}");
            client.DefaultRequestHeaders.Add("User-Agent", _userAgent);

            return client;
        }

        /// <summary>
        ///     Determine whether a file exists in a repo at a specified branch and
        ///     returns the SHA of the file if it does.
        /// </summary>
        /// <param name="repoUri">Repository URI</param>
        /// <param name="filePath">Path to file</param>
        /// <param name="branch">Branch</param>
        /// <returns>Sha of file or null if the file does not exist.</returns>
        public async Task<string> CheckIfFileExistsAsync(string repoUri, string filePath, string branch)
        {
            string commit;
            (string owner, string repo) = ParseRepoUri(repoUri);
            HttpResponseMessage response;

            try
            {
                JObject content;
                using (response = await this.ExecuteRemoteGitCommandAsync(
                    HttpMethod.Get,
                    $"repos/{owner}/{repo}/contents/{filePath}?ref={branch}",
                    _logger))
                {
                    content = JObject.Parse(await response.Content.ReadAsStringAsync());
                }
                commit = content["sha"].ToString();

                return commit;
            }
            catch (HttpRequestException exc) when (exc.Message.Contains(((int) HttpStatusCode.NotFound).ToString()))
            {
                return null;
            }
        }

        /// <summary>
        ///     Get the latest commit in a repo on the specific branch 
        /// </summary>
        /// <param name="repoUri">Repository uri</param>
        /// <param name="branch">Branch to retrieve the latest sha for</param>
        /// <returns>Latest sha.  Nulls if no commits were found.</returns>
        public Task<string> GetLastCommitShaAsync(string repoUri, string branch)
        {
            (string owner, string repo) = ParseRepoUri(repoUri);
            return GetLastCommitShaAsync(owner, repo, branch);
        }

        /// <summary>
        ///     Get the latest commit in a repo on the specific branch 
        /// </summary>
        /// <param name="owner">Owner of repo</param>
        /// <param name="repo">Repository name</param>
        /// <param name="branch">Branch to retrieve the latest sha for</param>
        /// <returns>Latest sha.  Throws if no commits were found.</returns>
        private async Task<string> GetLastCommitShaAsync(string owner, string repo, string branch)
        {
            try
            {
                JObject content;
                using (HttpResponseMessage response = await this.ExecuteRemoteGitCommandAsync(
                    HttpMethod.Get,
                    $"repos/{owner}/{repo}/commits/{branch}",
                    _logger))
                {
                    content = JObject.Parse(await response.Content.ReadAsStringAsync());
                }

                return content["sha"].ToString();
            }
            catch (HttpRequestException exc) when (exc.Message.Contains(((int)HttpStatusCode.NotFound).ToString()))
            {
                return null;
            }
        }

        /// <summary>
        /// Retrieve the list of status checks on a PR.
        /// </summary>
        /// <param name="pullRequestUrl">Uri of pull request</param>
        /// <returns>List of status checks.</returns>
        public async Task<IList<Check>> GetPullRequestChecksAsync(string pullRequestUrl)
        {
            (string owner, string repo, int id) = ParsePullRequestUri(pullRequestUrl);

            var commits = await Client.Repository.PullRequest.Commits(owner, repo, id);
            var lastCommitSha = commits.Last().Sha;

            return (await GetChecksFromStatusApiAsync(owner, repo, lastCommitSha))
                .Concat(await GetChecksFromChecksApiAsync(owner, repo, lastCommitSha))
                .ToList();
        }

        /// <summary>
        ///     Retrieve the list of reviews on a PR
        /// </summary>
        /// <param name="pullRequestUrl">Uri of pull request</param>
        /// <returns>List of reviews.</returns>
        public async Task<IList<Review>> GetPullRequestReviewsAsync(string pullRequestUrl)
        {
            (string owner, string repo, int id) = ParsePullRequestUri(pullRequestUrl);

            var reviews = await Client.Repository.PullRequest.Review.GetAll(owner, repo, id);
            return reviews.Select(review =>
                new Review(TranslateReviewState(review.State.Value), pullRequestUrl)).ToList();
        }

        private ReviewState TranslateReviewState(PullRequestReviewState state)
        {
            switch (state)
            {
                case PullRequestReviewState.Approved:
                    return ReviewState.Approved;
                case PullRequestReviewState.ChangesRequested:
                    return ReviewState.ChangesRequested;
                case PullRequestReviewState.Commented:
                    return ReviewState.Commented;
                case PullRequestReviewState.Dismissed:
                    return ReviewState.Rejected;
                case PullRequestReviewState.Pending:
                    return ReviewState.Pending;
                default:
                    throw new NotImplementedException($"Unexpected pull request review state {state}");
            }
        }

        private async Task<IList<Check>> GetChecksFromStatusApiAsync(string owner, string repo, string @ref)
        {
            var status = await Client.Repository.Status.GetCombined(owner, repo, @ref);

            return status.Statuses.Select(
                    s =>
                    {
                        var name = s.Context;
                        var url = s.TargetUrl;
                        CheckState state;
                        switch (s.State.Value)
                        {
                            case CommitState.Pending:
                                state = CheckState.Pending;
                                break;
                            case CommitState.Error:
                                state = CheckState.Error;
                                break;
                            case CommitState.Failure:
                                state = CheckState.Failure;
                                break;
                            case CommitState.Success:
                                state = CheckState.Success;
                                break;
                            default:
                                state = CheckState.None;
                                break;
                        }

                        return new Check(state, name, url);
                    })
                .ToList();
        }

        private async Task<IList<Check>> GetChecksFromChecksApiAsync(string owner, string repo, string @ref)
        {
            var checkRuns = await Client.Check.Run.GetAllForReference(owner, repo, @ref);
            return checkRuns.CheckRuns.Select(
                run =>
                {
                    var name = run.Name;
                    var externalID = run.ExternalId;
                    var url = run.HtmlUrl;
                    CheckState state;
                    switch (run.Status.Value)
                    {
                        case CheckStatus.Queued:
                        case CheckStatus.InProgress:
                            state = CheckState.Pending;
                            break;
                        case CheckStatus.Completed:
                            switch (run.Conclusion?.Value)
                            {
                                case CheckConclusion.Success:
                                    state = CheckState.Success;
                                    break;
                                case CheckConclusion.ActionRequired:
                                case CheckConclusion.Cancelled:
                                case CheckConclusion.Failure:
                                case CheckConclusion.Neutral:
                                case CheckConclusion.TimedOut:
                                    state = CheckState.Failure;
                                    break;
                                default:
                                    state = CheckState.None;
                                    break;
                            }

                            break;
                        default:
                            state = CheckState.None;
                            break;
                    }

                    return new Check(state, name, url, true);
                })
                .ToList();
        }

        private Octokit.GitHubClient CreateGitHubClientClient()
        {
            return new Octokit.GitHubClient(_product) {Credentials = new Octokit.Credentials(_personalAccessToken)};
        }

        private async Task<TreeResponse> GetRecursiveTreeAsync(string owner, string repo, string treeSha)
        {
            TreeResponse tree = await Client.Git.Tree.GetRecursive(owner, repo, treeSha);
            if (tree.Truncated)
            {
                throw new NotSupportedException(
                    $"The git repository is too large for the github api. Getting recursive tree '{treeSha}' returned truncated results.");
            }

            return tree;
        }

        private async Task<TreeResponse> GetTreeForPathAsync(string owner, string repo, string commitSha, string path)
        {
            var pathSegments = new Queue<string>(path.Split('/', '\\'));
            var currentPath = new List<string>();
            Octokit.Commit commit = await Client.Git.Commit.Get(owner, repo, commitSha);

            string treeSha = commit.Tree.Sha;

            while (true)
            {
                TreeResponse tree = await Client.Git.Tree.Get(owner, repo, treeSha);
                if (tree.Truncated)
                {
                    throw new NotSupportedException(
                        $"The git repository is too large for the github api. Getting tree '{treeSha}' returned truncated results.");
                }

                if (pathSegments.Count < 1)
                {
                    return tree;
                }

                string subfolder = pathSegments.Dequeue();
                currentPath.Add(subfolder);
                TreeItem subfolderItem = tree.Tree.Where(ti => ti.Type == TreeType.Tree)
                    .FirstOrDefault(ti => ti.Path == subfolder);
                if (subfolderItem == null)
                {
                    throw new DirectoryNotFoundException(
                        $"The path '{string.Join("/", currentPath)}' could not be found.");
                }

                treeSha = subfolderItem.Sha;
            }
        }

        public async Task GetCommitMapForPathAsync(
            string repoUri,
            string branch,
            string assetsProducedInCommit,
            List<GitFile> files,
            string pullRequestBaseBranch,
            string path = "eng/common/")
        {
            if (path.EndsWith("/"))
            {
                path = path.Substring(0, path.Length - 1);
            }

            _logger.LogInformation(
                $"Getting the contents of file/files in '{path}' of repo '{repoUri}' at commit '{assetsProducedInCommit}'");

            (string owner, string repo) = ParseRepoUri(repoUri);
            List<GitHubContent> contents;

            using (HttpResponseMessage response = await this.ExecuteRemoteGitCommandAsync(
                HttpMethod.Get,
                $"repos/{owner}/{repo}/contents/{path}?ref={assetsProducedInCommit}",
                _logger))
            {
                contents = JsonConvert.DeserializeObject<List<GitHubContent>>(await response.Content.ReadAsStringAsync());
            }

            foreach (GitHubContent content in contents)
            {
                if (content.Type == GitHubContentType.File)
                {
                    if (!GitFileManager.DependencyFiles.Contains(content.Path))
                    {
                        string fileContent = await GetFileContentsAsync(owner, repo, content.Path);
                        var gitCommit = new GitFile(content.Path, fileContent);
                        files.Add(gitCommit);
                    }
                }
                else
                {
                    await GetCommitMapForPathAsync(
                        repoUri,
                        branch,
                        assetsProducedInCommit,
                        files,
                        pullRequestBaseBranch,
                        content.Path);
                }
            }

            _logger.LogInformation(
                $"Getting the contents of file/files in '{path}' of repo '{repoUri}' at commit '{assetsProducedInCommit}' succeeded!");
        }

        /// <summary>
        ///     Parse out the owner and repo from a repository url
        /// </summary>
        /// <param name="uri">Github repository URL</param>
        /// <returns>Tuple of owner and repo</returns>
        public static (string owner, string repo) ParseRepoUri(string uri)
        {
            var u = new UriBuilder(uri);
            Match match = RepositoryUriPattern.Match(u.Path);
            if (!match.Success)
            {
                return default;
            }

            return (match.Groups["owner"].Value, match.Groups["repo"].Value);
        }

        /// <summary>
        ///     Parse out a pull request url into its component parts.
        /// </summary>
        /// <param name="uri">Github pr URL</param>
        /// <returns>Tuple of owner, repo and pr id</returns>
        public static (string owner, string repo, int id) ParsePullRequestUri(string uri)
        {
            var u = new UriBuilder(uri);
            Match match = PullRequestUriPattern.Match(u.Path);
            if (!match.Success)
            {
                return default;
            }

            return (match.Groups["owner"].Value, match.Groups["repo"].Value, int.Parse(match.Groups["id"].Value));
        }

        /// <summary>
        ///     Commit or update a set of files to a repo
        /// </summary>
        /// <param name="filesToCommit">Files to comit</param>
        /// <param name="repoUri">Remote repository URI</param>
        /// <param name="branch">Branch to push to</param>
        /// <param name="commitMessage">Commit message</param>
        /// <returns></returns>
        public Task CommitFilesAsync(List<GitFile> filesToCommit, string repoUri, string branch, string commitMessage)
        {
            return this.CommitFilesAsync(
                filesToCommit, 
                repoUri, 
                branch, 
                commitMessage, 
                _logger, 
                _personalAccessToken, 
                "dotnet-maestro[bot]", 
                "dotnet-maestro[bot]@users.noreply.github.com");
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
            _logger.LogInformation(
                $"Diffing '{baseVersion}'->'{targetVersion}' in {repoUri}");
            (string owner, string repo) = ParseRepoUri(repoUri);

            try
            {
                JObject content;
                using (HttpResponseMessage response = await this.ExecuteRemoteGitCommandAsync(
                    HttpMethod.Get,
                    $"repos/{owner}/{repo}/compare/{baseVersion}...{targetVersion}",
                    _logger))
                {
                    content = JObject.Parse(await response.Content.ReadAsStringAsync());
                }

                return new GitDiff()
                {
                    BaseVersion = baseVersion,
                    TargetVersion = targetVersion,
                    Ahead = content["ahead_by"].Value<int>(),
                    Behind = content["behind_by"].Value<int>(),
                    Valid = true
                };
            }
            catch (HttpRequestException reqEx) when (reqEx.Message.Contains("404 (Not Found)"))
            {
                return GitDiff.UnknownDiff();
            }
        }

        /// <summary>
        ///     Clone a remote repository.
        /// </summary>
        /// <param name="repoUri">Repository uri to clone</param>
        /// <param name="commit">Commit, branch, or tag to checkout</param>
        /// <param name="targetDirectory">Directory to clone into</param>
        /// <param name="gitDirectory">Location for the .git directory, or null for default</param>
        /// <returns></returns>
        public void Clone(string repoUri, string commit, string targetDirectory, string gitDirectory = null)
        {
            this.Clone(repoUri, commit, targetDirectory, _logger, _personalAccessToken, gitDirectory);
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
        public void AddRemoteIfMissing(string repoDir, string repoUrl)
        {
            throw new NotImplementedException($"Cannot add a remote to a remote repo.");
        }

        /// <summary>
        /// Checks that a repository exists
        /// </summary>
        /// <param name="repoUri">Repository uri</param>
        /// <returns>True if the repository exists, false otherwise.</returns>
        public async Task<bool> RepoExistsAsync(string repoUri)
        {
            (string owner, string repo) = ParseRepoUri(repoUri);

            try
            {
                using (await this.ExecuteRemoteGitCommandAsync(
                      HttpMethod.Get,
                      $"repos/{owner}/{repo}",
                      _logger,
                      logFailure: false)) { }
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
            (string owner, string repo, int id) prInfo = ParsePullRequestUri(pullRequestUri);
            await DeleteBranchAsync(prInfo.owner, prInfo.repo, pr.HeadBranch);
        }
    }
}
