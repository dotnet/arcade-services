// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;

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
        await ValidateCommits(cancellationToken);
    }

    private async Task ExecutePush(CancellationToken cancellationToken)
    {
        var vmrUrl = "https://github.com/MilenaHristova/individual-repo";

        await _processManager.ExecuteGit(_vmrInfo.VmrPath, "config", "user.email", Constants.DarcBotEmail);
        await _processManager.ExecuteGit(_vmrInfo.VmrPath, "config", "user.name", Constants.DarcBotName);

        await _processManager.ExecuteGit(
            _vmrInfo.VmrPath,
            new string[] { "remote", "add", "dotnet", vmrUrl },
            cancellationToken: cancellationToken);

        await _processManager.ExecuteGit(
            _vmrInfo.VmrPath,
            new string[] { "fetch", "dotnet" },
            cancellationToken: cancellationToken);

        await _processManager.ExecuteGit(
            _vmrInfo.VmrPath,
            new string[] { "branch", "main" },
            cancellationToken: cancellationToken);

        await _processManager.ExecuteGit(
            _vmrInfo.VmrPath,
            new string[] { "branch", "--set-upstream-to=dotnet/main", "main" },
            cancellationToken: cancellationToken);

        await _processManager.ExecuteGit(
            _vmrInfo.VmrPath,
            new string[] { "push", "dotnet", "main" },
            cancellationToken: cancellationToken);
    }

    private async Task ValidateCommits(CancellationToken cancellationToken)
    {
        var publicRepos = _sourceManifest
            .Repositories
            .ToDictionary(r => r.Path.Replace("-", ""), r => $"{r.RemoteUri}/commit/{r.CommitSha}");

        var commits = await GetRepositoriesCommits(publicRepos, cancellationToken);

    }

    private async Task<CommitsQueryResult> GetRepositoriesCommits(
        Dictionary<string, string> repos,
        CancellationToken cancellationToken)
    {
        var queries = repos
            .Select(r => $@"{r.Key}: resource(url: \""{r.Value}\""){{ ... on Commit {{id}}}}");

        var query = string.Join(", ", queries);
        var body = @"{""query"" : ""{" + query + @"}""}";

        HttpResponseMessage response = null;

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
            var result = JsonSerializer.Deserialize<CommitsQueryResult>(content);
            return result;
        }
        catch (HttpRequestException exc)
        {
            _logger.LogError(exc.Message);
            return null;
        }
    }

    private record CommitsQueryResult(Dictionary<string, Dictionary<string, string>> data);
}
