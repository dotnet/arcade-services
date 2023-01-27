// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;
using Octokit;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IVmrPusher
{
    Task Push(string remoteUrl, string branch, bool verifyCommits, string? gitHubApiPat, CancellationToken cancellationToken);
}

public class VmrPusher : IVmrPusher
{
    private readonly IVmrInfo _vmrInfo;
    private readonly ILogger _logger;
    private readonly ISourceManifest _sourceManifest;
    private readonly ILocalGitRepo _localGitRepo;
    private readonly HttpClient _httpClient;
    private readonly VmrRemoteConfiguration _vmrRemoteConfiguration;
    private const string GraphQLUrl = "https://api.github.com/graphql";

    public VmrPusher( 
        IVmrInfo vmrInfo, 
        ILogger logger,
        ISourceManifest sourceManifest,
        IHttpClientFactory httpClientFactory,
        ILocalGitRepo localGitRepo,
        VmrRemoteConfiguration config)
    {
        _vmrInfo = vmrInfo;
        _logger = logger;
        _sourceManifest = sourceManifest;
        _vmrRemoteConfiguration = config;
        _localGitRepo = localGitRepo;
        _httpClient = httpClientFactory.CreateClient("GraphQL");
    }

    /// <summary>
    /// Pushes the specified branch to the specified remote. If verifyCommits is true, checks that each commit is present in a public repo 
    /// before pushing
    /// </summary>
    /// <param name="remoteUrl">URL to push to</param>
    /// <param name="branch">Name of an already existing branch to push</param>
    /// <param name="verifyCommits">Verify that all commits are found in public repos before pushing</param>
    /// <param name="gitHubApiPat">Token with no scopes for authenticating to GitHub GraphQL API.</param>
    public async Task Push(string remoteUrl, string branch, bool verifyCommits, string? gitHubApiPat, CancellationToken cancellationToken)
    {
        if (verifyCommits)
        {
            if(gitHubApiPat == null)
            {
                throw new Exception("Please specify a GitHub token with basic scope to be used for authenticating to GitHub GraphQL API.");
            }

            if(!await CheckCommitAvailability(gitHubApiPat, cancellationToken))
            {
                throw new Exception("Not all pushed commits are publicly available");
            }
        }

        var repoType = GitRepoTypeParser.ParseFromUri(remoteUrl);

        string? pat = string.Empty;
        if (repoType == GitRepoType.GitHub)
        {
            pat = _vmrRemoteConfiguration.GitHubToken;
        }
        if (repoType == GitRepoType.AzureDevOps)
        {
            pat = _vmrRemoteConfiguration.AzureDevOpsToken;
        }

        _localGitRepo.Push(
            _vmrInfo.VmrPath,
            branch,
            remoteUrl,
            pat,
            new LibGit2Sharp.Identity(Constants.DarcBotName, Constants.DarcBotEmail));
    }

    private async Task<bool> CheckCommitAvailability(string gitHubApiPat, CancellationToken cancellationToken)
    {
        var commitsSearchArguments = _sourceManifest
            .Repositories
            .Select(r => (GitRepoUrlParser.GetRepoNameAndOwner(r.RemoteUri), r.CommitSha))
            .Select(r => new CommitSearchArguments(r.Item1.RepoName, r.Item1.Org, r.CommitSha));

        var commits = await GetGitHubCommits(commitsSearchArguments, gitHubApiPat, cancellationToken);
        
        if (commits == null || commits.Data == null)
        {
            _logger.LogError("The Graphql query is not in the correct format.");
            return false;
        }

        foreach (var pair in commits.Data)
        {
            if (pair.Value == null)
            {
                var commitArgs = commitsSearchArguments.First(r => GetGraphQlIdentifier(r.RepoName) == pair.Key);
                _logger.LogError($"Repository {Constants.GitHubUrlPrefix}{commitArgs.RepoOwner}/{commitArgs.RepoName} was not found!");
                return false;
            }

            if (pair.Value.Object == null)
            {
                var commitArgs = commitsSearchArguments.First(r => GetGraphQlIdentifier(r.RepoName) == pair.Key);
                _logger.LogError($"Commit {commitArgs.Sha} was not found in {Constants.GitHubUrlPrefix}{commitArgs.RepoOwner}/{commitArgs.RepoName}!");
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Sends a query to GitHub GraphQL API searching for all commits in their respective public repos in one joined request
    /// The result of each search is a property in the "data" object returned by the API
    /// where Data.RepoName.Object.Id is a string if the commit was found in "RepoName"
    /// and Data.RepoName.Object is null if the commit was not found
    /// </summary>
    private async Task<CommitsQueryResult?> GetGitHubCommits(
        IEnumerable<CommitSearchArguments> commits,
        string gitHubApiPat,
        CancellationToken cancellationToken)
    {
        var queries = commits
            .Select(c => $"{GetGraphQlIdentifier(c.RepoName)}: repository(name: \\\"{c.RepoName}\\\", owner: \\\"{c.RepoOwner}\\\"){{object(oid: \\\"{c.Sha}\\\"){{ ... on Commit {{id}}}}}}");
        var query = string.Join(", ", queries);
        var body = @"{""query"" : ""{" + query + @"}""}";

        _logger.LogDebug("Sending GraphQL query: {query}", body);

        var httpRequestManager = new HttpRequestManager(
            _httpClient,
            HttpMethod.Post,
            GraphQLUrl,
            _logger,
            body,
            authHeader: new AuthenticationHeaderValue("Token", gitHubApiPat));

        using var response = await httpRequestManager.ExecuteAsync(0);

        var content = response.Content.ReadAsStringAsync(cancellationToken).Result;
        var settings = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        return JsonSerializer.Deserialize<CommitsQueryResult>(content, settings);
    }

    private static string GetGraphQlIdentifier(string repoName) => repoName.Replace("-", null).Replace(".", null);

    private record CommitSearchArguments(string RepoName, string RepoOwner, string Sha);
    private record CommitsQueryResult(Dictionary<string, CommitResult>? Data);
    private record CommitObject(string Id);
    private record CommitResult(CommitObject? Object);
}
