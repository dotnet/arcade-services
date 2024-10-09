// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel.DataAnnotations;
using Maestro.Api.Model.v2020_02_20;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib;
using ProductConstructionService.DependencyFlow.WorkItems;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.Api.Api.v2020_02_20.Controllers;

[Route("codeflow")]
[ApiVersion("2020-02-20")]
public class CodeFlowController(
        IBasicBarClient barClient,
        IWorkItemProducerFactory workItemProducerFactory)
    : ControllerBase
{
    private readonly IBasicBarClient _barClient = barClient;
    private readonly IWorkItemProducerFactory _workItemProducerFactory = workItemProducerFactory;

    [HttpPost(Name = "Flow")]
    public async Task<IActionResult> FlowBuild([Required, FromBody] CodeFlowRequest request)
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
