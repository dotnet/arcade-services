// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client.Models;
using ProductConstructionService.Api.Queue;
using ProductConstructionService.Api.Queue.Jobs;

namespace ProductConstructionService.Api.Controllers;

[Route("codeflow")]
public class CodeFlowController(
        IBasicBarClient barClient,
        JobProducerFactory jobProducerFactory)
    : Controller
{
    private readonly IBasicBarClient _barClient = barClient;
    private readonly JobProducerFactory _jobProducerFactory = jobProducerFactory;

    [HttpPost("create-branch")]
    public async Task<IActionResult> CreateBranch(string subscriptionId, int buildId)
    {
        if (!Guid.TryParse(subscriptionId, out Guid subId))
        {
            return BadRequest("Provided subscription ID is not a GUID");
        }

        Subscription subscription = await _barClient.GetSubscriptionAsync(subId);
        if (subscription == null)
        {
            return NotFound($"Subscription {subscriptionId} not found");
        }

        Build build = await _barClient.GetBuildAsync(buildId);
        if (build == null)
        {
            return NotFound($"Build {buildId} not found");
        }

        await _jobProducerFactory.Create<CodeFlowJob>().ProduceJobAsync(new()
        {
            BuildId = buildId,
            SubscriptionId = subscriptionId,
        });

        return Ok();
    }
}
