// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using Kusto.Cloud.Platform.Utils;
using Maestro.Api.Model.v2020_02_20;
using Maestro.Common;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.Maestro.Client.Models;
using NRedisStack.Graph.DataTypes;
using Octokit;
using ProductConstructionService.Api.VirtualMonoRepo;
using ProductConstructionService.DependencyFlow.WorkItems;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.Api.Api.v2020_02_20.Controllers;

[Route("codeflow")]
[ApiVersion("2020-02-20")]
public class CodeFlowController(
        IBasicBarClient barClient,
        IWorkItemProducerFactory workItemProducerFactory,
        IGitRepoFactory gitRepoFactory,
        IHttpClientFactory httpClientFactory)
    : ControllerBase
{
    private readonly IBasicBarClient _barClient = barClient;
    private readonly IWorkItemProducerFactory _workItemProducerFactory = workItemProducerFactory;
    private readonly IGitRepoFactory _gitRepoFactory = gitRepoFactory;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;

    [HttpPost(Name = "Flow")]
    public async Task<IActionResult> FlowBuild([Required, FromBody] Maestro.Api.Model.v2020_02_20.CodeFlowRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        Microsoft.DotNet.Maestro.Client.Models.Subscription subscription = await _barClient.GetSubscriptionAsync(request.SubscriptionId);
        if (subscription == null)
        {
            return NotFound($"Subscription {request.SubscriptionId} not found");
        }

        Microsoft.DotNet.Maestro.Client.Models.Build build = await _barClient.GetBuildAsync(request.BuildId);
        if (build == null)
        {
            return NotFound($"Build {request.BuildId} not found");
        }

        await _workItemProducerFactory
            .CreateProducer<CodeFlowWorkItem>()
            .ProduceWorkItemAsync(new()
            {
                BuildId = request.BuildId,
                SubscriptionId = request.SubscriptionId,
                PrBranch = request.PrBranch,
                PrUrl = request.PrUrl,
            });

        return Ok();
    }

    [HttpGet("status")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(List<SyncedCommit>), Description = "List of Synced Commits")]
    public async Task<IActionResult> GetCodeFlowStatus(string branch)
    {
        string vmrUrl = "https://github.com/dotnet/dotnet";
        string sourceManifestFilePath = "src/source-manifest.json";

        IGitRepo vmrRepo = _gitRepoFactory.CreateClient(vmrUrl);

        var options = new JsonSerializerOptions
        {
            AllowTrailingCommas = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReadCommentHandling = JsonCommentHandling.Skip,
            PropertyNameCaseInsensitive = true
        };

        string sourceManifestContent = await vmrRepo.GetFileContentsAsync(sourceManifestFilePath, vmrUrl, branch);

        var wrapper = JsonSerializer.Deserialize<SourceManifestWrapper>(sourceManifestContent, options)
            ?? throw new Exception($"Failed to deserialize source-manifest.json");

        var commitsSearchArguments = wrapper
            .Repositories
            .Select(r => (GitRepoUrlParser.GetRepoNameAndOwner(r.RemoteUri), r.CommitSha))
            .Select(r => new CommitSearchArguments(r.Item1.RepoName, r.Item1.Org, r.CommitSha));

        var commits = await GetGitHubCommits(commitsSearchArguments, CancellationToken.None);

        var responseData = wrapper.Repositories
            .Select(r => new SyncedCommit
            {
                RepoPath = r.Path,
                CommitUrl = $"{r.RemoteUri}/commit/{r.CommitSha}",
                DateCommitted = commits?.Data?[GetGraphQlIdentifier(GitRepoUrlParser.GetRepoNameAndOwner(r.RemoteUri).RepoName)].Object?.CommittedDate
            });

        return Ok(responseData);
    }

    private async Task<CommitsQueryResult?> GetGitHubCommits(
        IEnumerable<CommitSearchArguments> commits,
        CancellationToken cancellationToken)
    {
        var queries = commits
            .Select(c => $"{GetGraphQlIdentifier(c.RepoName)}: repository(name: \\\"{c.RepoName}\\\", owner: \\\"{c.RepoOwner}\\\"){{object(oid: \\\"{c.Sha}\\\"){{ ... on Commit {{committedDate}}}}}}");
        var query = string.Join(", ", queries);
        var body = "{\"query\" : \"{" + query + "}\"}";

        using var httpClient = _httpClientFactory.CreateClient("GraphQL");

        HttpResponseMessage response;

        using (HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Post, "https://api.github.com/graphql"))
        {
            message.Content = new StringContent(body, Encoding.UTF8, "application/json");

            response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseContentRead);
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var settings = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        return JsonSerializer.Deserialize<CommitsQueryResult>(content, settings);
    }

    private static string GetGraphQlIdentifier(string repoName) => repoName.Replace("-", null).Replace(".", null);

    private class SourceManifestWrapper
    {
        public ICollection<RepositoryRecord> Repositories { get; init; } = [];
        public ICollection<SubmoduleRecord> Submodules { get; init; } = [];
    }

    private record CommitSearchArguments(string RepoName, string RepoOwner, string Sha);
    private record CommitsQueryResult(Dictionary<string, CommitResult>? Data);
    private record CommitObject(string CommittedDate);
    private record CommitResult(CommitObject? Object);
}
