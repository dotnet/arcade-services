// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LibGit2Sharp;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IVmrPusher
{
    Task Push(string remote, string branch, string gitHubApiPat, CancellationToken cancellationToken);
}

public class VmrPusher : IVmrPusher
{
    private readonly IVmrInfo _vmrInfo;
    private readonly ILogger _logger;
    private readonly ISourceManifest _sourceManifest;
    private readonly HttpClient _httpClient;
    private readonly VmrRemoteConfiguration _vmrRemoteConfiguration;
    private const string GraphQLUrl = "https://api.github.com/graphql";

    public VmrPusher( 
        IVmrInfo vmrInfo, 
        ILogger logger,
        ISourceManifest sourceManifest,
        IHttpClientFactory httpClientFactory,
        VmrRemoteConfiguration config)
    {
        _vmrInfo = vmrInfo;
        _logger = logger;
        _sourceManifest = sourceManifest;
        _vmrRemoteConfiguration = config;
        _httpClient = httpClientFactory.CreateClient();
    }

    public async Task Push(string remote, string branch, string gitHubApiPat, CancellationToken cancellationToken)
    {
        if (await CheckCommitAvailability(gitHubApiPat, cancellationToken))
        {
            ExecutePush(remote, branch);
        }
        else
        {
            _logger.LogError("Pushing to the VMR failed. Not all pushed commits are publicly available");
        }  
    }

    private void ExecutePush(
        string remoteName,
        string branchName)
    {
        using var vmrRepo = new Repository(_vmrInfo.VmrPath);
        vmrRepo.Config.Add("user.name", Constants.DarcBotName);
        vmrRepo.Config.Add("user.email", Constants.DarcBotEmail);

        var remote = vmrRepo.Network.Remotes[remoteName];

        Branch branch;
        if (!vmrRepo.Branches.Any(b => b.FriendlyName == branchName))
        {
            branch = vmrRepo.CreateBranch(branchName);
        }
        else
        {
            branch = vmrRepo.Branches[branchName];
        }

        var pushOptions = new PushOptions
        {
            CredentialsProvider = (url, user, cred) =>
                new UsernamePasswordCredentials
                {
                    Username = _vmrRemoteConfiguration.GitHubToken,
                    Password = string.Empty
                }
        };

        vmrRepo.Network.Push(remote, branch.CanonicalName, pushOptions);
    }

    private async Task<bool> CheckCommitAvailability(string gitHubApiPat, CancellationToken cancellationToken)
    {
        var commitsSearchArguments = _sourceManifest
            .Repositories
            .Select(r => GetCommitSearchArguments(r.RemoteUri, r.CommitSha));

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

    private async Task<CommitsQueryResult?> GetGitHubCommits(
        IEnumerable<CommitSearchArguments> commits,
        string gitHubApiPat,
        CancellationToken cancellationToken)
    {
        var queries = commits
            .Select(c => $@"{GetGraphQlIdentifier(c.RepoName)}: repository(name: \""{c.RepoName}\"", owner: \""{c.RepoOwner}\""){{object(oid: \""{c.Sha}\""){{ ... on Commit {{id}}}}}}");
        var query = string.Join(", ", queries);
        var body = @"{""query"" : ""{" + query + @"}""}";

        _logger.LogDebug("Sending GraphQL query: {query}", body);

        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Darc", null));

        using HttpRequestMessage message = new(HttpMethod.Post, GraphQLUrl);
        message.Content = new StringContent(body, Encoding.UTF8, "application/json");
        message.Headers.Authorization = new AuthenticationHeaderValue("Token", gitHubApiPat);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(message, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException)
        {
            _logger.LogError("Cannot connect to GraphQL API");
            throw;
        }

        var content = response.Content.ReadAsStringAsync(cancellationToken).Result;
        var settings = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        var result = JsonSerializer.Deserialize<CommitsQueryResult>(content, settings);
        return result;
    }

    private CommitSearchArguments GetCommitSearchArguments(string uri, string sha)
    {
        var repoType = GitRepoTypeParser.ParseFromUri(uri);
        
        if (repoType == GitRepoType.AzureDevOps)
        {
            string[] repoParts = uri.Substring(uri.LastIndexOf('/')).Split('-', 2);

            if (repoParts.Length != 2)
            {
                throw new Exception($"Invalid URI in source manifest. Repo '{uri}' does not end with the expected <GH organization>-<GH repo> format.");
            }

            string org = repoParts[0];
            string repo = repoParts[1];

            // The internal Nuget.Client repo has suffix which needs to be accounted for.
            const string trustedSuffix = "-Trusted";
            if (uri.EndsWith(trustedSuffix, StringComparison.OrdinalIgnoreCase))
            {
                repo = repo.Substring(0, repo.Length - trustedSuffix.Length);
            }

            return new CommitSearchArguments(repo, org, sha);
        }
        else if (repoType == GitRepoType.GitHub)
        {
            string[] repoParts = uri.Substring(Constants.GitHubUrlPrefix.Length).Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (repoParts.Length != 2)
            {
                throw new Exception($"Invalid URI in source manifest. Repo '{uri}' does not end with the expected <GH organization>/<GH repo> format.");
            }

            return new CommitSearchArguments(repoParts[1], repoParts[0], sha);
        }

        throw new Exception("Unsupported format of repository url " + uri);
    }

    private string GetGraphQlIdentifier(string repoName) => repoName.Replace("-", string.Empty).Replace(".", string.Empty);

    private record CommitSearchArguments(string RepoName, string RepoOwner, string Sha);
    private record CommitsQueryResult(Dictionary<string, CommitResult>? Data);
    private record CommitObject(string Id);
    private record CommitResult(CommitObject? Object);
}
