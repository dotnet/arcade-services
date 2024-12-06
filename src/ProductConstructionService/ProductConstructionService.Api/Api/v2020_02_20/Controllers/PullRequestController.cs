// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Mvc;
using ProductConstructionService.Common;

namespace ProductConstructionService.Api.Api.v2020_02_20.Controllers;

[Route("pull-requests")]
[ApiVersion("2020-02-20")]
public class PullRequestController : ControllerBase
{
    private readonly IRedisCacheFactory _cacheFactory;

    public PullRequestController(IRedisCacheFactory cacheFactory)
    {
        _cacheFactory = cacheFactory;
    }

    [HttpGet("tracked")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(List<TrackedPullRequest>), Description = "The list of currently tracked pull requests by the service")]
    [ValidateModelState]
    public async Task<IActionResult> GetTrackedPullRequests()
    {
        var cache = _cacheFactory.Create("InProgressPullRequest_");

        var prs = new List<TrackedPullRequest>();
        await foreach (var key in cache.GetKeysAsync("InProgressPullRequest_*"))
        {
            var pr = await _cacheFactory
                .Create<TrackedPullRequest>(key, includeTypeInKey: false)
                .TryGetStateAsync();

            if (pr != null)
            {
                prs.Add(pr);
            }
        }

        return Ok(prs.AsQueryable());
    }

    private record TrackedPullRequest(string Url);
}
