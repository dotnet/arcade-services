// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client.Models;
using ProductConstructionService.Common;
using ProductConstructionService.DependencyFlow;

namespace ProductConstructionService.Api.Api.v2020_02_20.Controllers;

[Route("pull-requests")]
[ApiVersion("2020-02-20")]
public class PullRequestController : ControllerBase
{
    private readonly IRedisCacheFactory _cacheFactory;
    private readonly IBasicBarClient _barClient;

    public PullRequestController(
        IRedisCacheFactory cacheFactory,
        IBasicBarClient barClient)
    {
        _cacheFactory = cacheFactory;
        _barClient = barClient;
    }

    [HttpGet("tracked")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(List<TrackedPullRequest>), Description = "The list of currently tracked pull requests by the service")]
    [ValidateModelState]
    public async Task<IActionResult> GetTrackedPullRequests()
    {
        var cache = _cacheFactory.Create(nameof(InProgressPullRequest) + "_");

        var prs = new List<TrackedPullRequest>();
        await foreach (var key in cache.GetKeysAsync(nameof(InProgressPullRequest) + "_*"))
        {
            var pr = await _cacheFactory
                .Create<InProgressPullRequest>(key, includeTypeInKey: false)
                .TryGetStateAsync();

            if (pr == null)
            {
                continue;
            }

            var subscriptions = pr.ContainedSubscriptions.Select(s => _barClient.GetSubscriptionAsync(s.SubscriptionId));

            await Task.WhenAll(subscriptions);

            var updates = subscriptions
                .Select(task => task.Result)
                .Select(update => new PullRequestUpdate(update.SourceRepository))
                .ToList();

            Subscription sampleUpdate = subscriptions.First().Result;

            var targetBranch = sampleUpdate.TargetBranch;
            var channel = sampleUpdate.Channel.Name;

            prs.Add(new TrackedPullRequest(pr.Url, channel, targetBranch, updates));
        }

        return Ok(prs.AsQueryable());
    }

    private record TrackedPullRequest(string Url, string Channel, string TargetBranch, List<PullRequestUpdate> Updates);

    private record PullRequestUpdate(string SourceRepository);
}
