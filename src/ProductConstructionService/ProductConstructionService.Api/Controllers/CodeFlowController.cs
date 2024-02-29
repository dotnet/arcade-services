// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client.Models;
using ProductConstructionService.Api.Controllers.Models;
using ProductConstructionService.Api.Queue;
using ProductConstructionService.Api.Queue.Jobs;

namespace ProductConstructionService.Api.Controllers;

[Route("codeflow")]
internal class CodeFlowController(
        IBasicBarClient barClient,
        JobProducerFactory jobProducerFactory)
    : InternalController
{
    private readonly IBasicBarClient _barClient = barClient;
    private readonly JobProducerFactory _jobProducerFactory = jobProducerFactory;

    [HttpPost]
    public async Task<IActionResult> CreateBranch([Required, FromBody] CreateBranchRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        Subscription subscription = await _barClient.GetSubscriptionAsync(request.SubscriptionId);
        if (subscription == null)
        {
            return NotFound($"Subscription {request.SubscriptionId} not found");
        }

        Build build = await _barClient.GetBuildAsync(request.BuildId);
        if (build == null)
        {
            return NotFound($"Build {request.BuildId} not found");
        }

        await _jobProducerFactory.Create<CodeFlowJob>().ProduceJobAsync(new()
        {
            BuildId = request.BuildId,
            SubscriptionId = request.SubscriptionId,
            TargetBranch = request.TargetBranch,
        });

        return Ok();
    }
}
