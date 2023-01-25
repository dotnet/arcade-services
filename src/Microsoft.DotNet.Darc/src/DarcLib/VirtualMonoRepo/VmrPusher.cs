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
    Task Push(string remote, string branch, bool verifyCommits, string? gitHubApiPat, CancellationToken cancellationToken);
}

public class VmrPusher : IVmrPusher
{
    private readonly IVmrInfo _vmrInfo;
    private readonly ILogger _logger;
    private readonly ISourceManifest _sourceManifest;
    private readonly HttpClient _httpClient;
    private readonly VmrRemoteConfiguration _vmrRemoteConfiguration;

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
        _httpClient = httpClientFactory.CreateClient("GraphQL");
    }

    public async Task Push(string remote, string branch, bool verifyCommits, string? gitHubApiPat, CancellationToken cancellationToken)
    {
        if (!verifyCommits ||
            (gitHubApiPat != null && await CheckCommitAvailability(gitHubApiPat, cancellationToken)))
        {
            ExecutePush(remote, branch);
        }
        else
        {
            _logger.LogError("Pushing to the VMR failed. Not all pushed commits are publicly available");
        }  
    }

    private void ExecutePush(string remoteName, string branchName)
    {
        using var vmrRepo = new Repository(_vmrInfo.VmrPath);
        vmrRepo.Config.Add("user.name", Constants.DarcBotName);
        vmrRepo.Config.Add("user.email", Constants.DarcBotEmail);

        var remote = vmrRepo.Network.Remotes[remoteName];
        if (remote == null)
        {
            throw new Exception($"No remote named {remoteName} found in VMR repo.");
        }

        var branch = vmrRepo.Branches[branchName];
        if (branch == null)
        {
            _logger.LogDebug($"No branch {branchName} found in VMR repo. Creating a new branch {branchName}");
            branch = vmrRepo.CreateBranch(branchName);
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
            string.Empty,
            _logger,
            body,
            authHeader: new AuthenticationHeaderValue("Token", gitHubApiPat));

        using var response = await httpRequestManager.ExecuteAsync(0);

        var content = response.Content.ReadAsStringAsync(cancellationToken).Result;
        var settings = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        var result = JsonSerializer.Deserialize<CommitsQueryResult>(content, settings);
        return result;
    }

    private static string GetGraphQlIdentifier(string repoName) => repoName.Replace("-", null).Replace(".", null);

    private record CommitSearchArguments(string RepoName, string RepoOwner, string Sha);
    private record CommitsQueryResult(Dictionary<string, CommitResult>? Data);
    private record CommitObject(string Id);
    private record CommitResult(CommitObject? Object);
}
