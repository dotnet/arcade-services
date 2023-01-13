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
using Microsoft.Extensions.Options;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IVmrPusher
{
    Task Push(CancellationToken cancellationToken);
}

public class VmrPusher : IVmrPusher
{
    private readonly IProcessManager _processManager;
    private readonly IVmrInfo _vmrInfo;
    private readonly ILogger _logger;
    private readonly ISourceManifest _sourceManifest;
    private readonly VmrRemoteConfiguration _vmrRemoteConfiguration;
   
    public VmrPusher(
        IProcessManager processManager, 
        IVmrInfo vmrInfo, 
        ILogger logger,
        ISourceManifest sourceManifest,
        VmrRemoteConfiguration config)
    {
        _processManager = processManager;
        _vmrInfo = vmrInfo;
        _logger = logger;
        _sourceManifest = sourceManifest;
        _vmrRemoteConfiguration = config;
    }

    public async Task Push(CancellationToken cancellationToken)
    {
        ExecutePush("main", cancellationToken);
    }

    private void ExecutePush(
        string branchName,
        CancellationToken cancellationToken)
    {
        var vmrUrl = "";
        var remoteName = "dotnet";
     
        using var vmrRepo = new Repository(_vmrInfo.VmrPath);
        vmrRepo.Config.Add("user.name", Constants.DarcBotName);
        vmrRepo.Config.Add("user.email", Constants.DarcBotEmail);

        vmrRepo.Network.Remotes.Update(remoteName, r => r.Url = vmrUrl);

        var remote = vmrRepo.Network.Remotes[remoteName];
        var refSpecs = remote.FetchRefSpecs.Select(x => x.Specification);
        
        try
        {
            Commands.Fetch(vmrRepo, remoteName, refSpecs, new FetchOptions(), "Fetching dotnet");
        }
        catch
        {
            _logger.LogWarning($"Fetching failed.");
        }

        Branch branch;
        if (!vmrRepo.Branches.Any(b => b.FriendlyName == branchName))
        {
            branch = vmrRepo.CreateBranch(branchName);
        }
        else
        {
            branch = vmrRepo.Branches[branchName];
        }

        vmrRepo.Branches.Update(branch, b => b.Remote = remoteName, b => b.UpstreamBranch = remoteName + "/" + branchName);

        var pushOptions = new PushOptions
        {
            CredentialsProvider = (url, user, cred) =>
                new UsernamePasswordCredentials
                {
                    Username = Constants.DarcBotName,
                    Password = _vmrRemoteConfiguration.GitHubToken
                }
        };

        vmrRepo.Network.Push(remote, branch.CanonicalName, pushOptions);
    }

    private async Task<bool> ValidateCommits(CancellationToken cancellationToken)
    {
        var publicRepos = _sourceManifest
            .Repositories
            .ToDictionary(r => r.Path.Replace("-", ""), r => GetPublicRepoCommitUri(r.RemoteUri, r.CommitSha));

        var commits = await GetRepositoriesCommits(publicRepos, cancellationToken);
        
        if(commits == null || commits.Data == null)
        {
            return false;
        }

        foreach(var pair in commits.Data)
        {
            if(pair.Value == null)
            {
                return false;
            }
        }

        return true;
    }

    private async Task<CommitsQueryResult?> GetRepositoriesCommits(
        Dictionary<string, string> repos,
        CancellationToken cancellationToken)
    {
        var queries = repos
            .Select(r => $@"{r.Key}: resource(url: \""{r.Value}\""){{ ... on Commit {{id}}}}");

        var query = string.Join(", ", queries);
        var body = @"{""query"" : ""{" + query + @"}""}";

        HttpResponseMessage? response = null;

        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AppName", "1.0"));

        using HttpRequestMessage message = new(HttpMethod.Post, "https://api.github.com/graphql");
        message.Content = new StringContent(body, Encoding.UTF8, "application/json");
        message.Headers.Authorization = new AuthenticationHeaderValue("Token", _vmrRemoteConfiguration.GitHubToken);

        try
        {
            response = await client.SendAsync(message, cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = response.Content.ReadAsStringAsync(cancellationToken).Result;
            var settings = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            };

            var result = JsonSerializer.Deserialize<CommitsQueryResult>(content, settings);
            return result;
        }
        catch (HttpRequestException exc)
        {
            _logger.LogError(exc.Message);
            return null;
        }
    }

    private string GetPublicRepoCommitUri(string uri, string commit)
    {
        if (uri.StartsWith("https://dev.azure.com", StringComparison.OrdinalIgnoreCase))
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

            uri = $"https://github.com/{org}/{repo}";
        }

        return $"{uri}/commit/{commit}";
    }

    private record CommitsQueryResult(Dictionary<string, Dictionary<string, string>>? Data);
}
