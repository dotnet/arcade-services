// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client.Models;
using ProductConstructionService.WorkItems;
using ProductConstructionService.WorkItems.WorkItemDefinitions;

namespace ProductConstructionService.Api.Api.v2020_02_20.Controllers;

[Route("codeflow")]
[ApiVersion("2020-02-20")]
public class CodeFlowController(
        IBasicBarClient barClient,
        WorkItemProducerFactory workItemProducerFactory)
    : ControllerBase
{
    private readonly IBasicBarClient _barClient = barClient;
    private readonly WorkItemProducerFactory _workItemProducerFactory = workItemProducerFactory;

    [HttpPost(Name = "Flow")]
    public async Task<IActionResult> FlowBuild([Required, FromBody] Maestro.Api.Model.v2020_02_20.CodeFlowRequest request)
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

        await _workItemProducerFactory.Create<CodeFlowWorkItem>().ProduceWorkItemAsync(new()
        {
            BuildId = request.BuildId,
            SubscriptionId = request.SubscriptionId,
            PrBranch = request.PrBranch,
            PrUrl = request.PrUrl,
        });

        return Ok();
    }
}
