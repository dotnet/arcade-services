// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProductConstructionService.Api.Configuration;
using Maestro.WorkItems;

namespace ProductConstructionService.Api.Controllers;

[Route("status")]
[ApiVersion("2020-02-20")]
[Authorize(Policy = AuthenticationConfiguration.AdminAuthorizationPolicyName)]
public class StatusController(
    IWorkItemProcessorReplicaProvider replicaProvider,
    IWorkItemProcessorStateStore stateStore) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet(Name = "Status")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(Dictionary<string, string>), Description = "Returns PCS replica states")]
    public async Task<IActionResult> GetPcsWorkItemProcessorStatus()
    {
        return Ok(await GetObservedStatesAsync(HttpContext.RequestAborted));
    }

    [HttpPut("start", Name = "Start")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(Dictionary<string, string>), Description = "Starts all PCS replicas")]
    public async Task<IActionResult> StartPcsWorkItemProcessors()
    {
        return Ok(await SetDesiredStateAsync(WorkItemProcessorState.Working, HttpContext.RequestAborted));
    }

    [HttpPut("stop", Name = "Stop")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(Dictionary<string, string>), Description = "Tells all PCS replicas to stop after finishing their current work item")]
    public async Task<IActionResult> StopPcsWorkItemProcessors()
    {
        return Ok(await SetDesiredStateAsync(WorkItemProcessorState.Stopped, HttpContext.RequestAborted));
    }

    private async Task<Dictionary<string, string>> SetDesiredStateAsync(WorkItemProcessorState state, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> replicaNames = await replicaProvider.GetReplicaNamesAsync();

        await Task.WhenAll(replicaNames.Select(replicaName => stateStore.SetDesiredStateAsync(replicaName, state, cancellationToken)));

        return await GetObservedStatesAsync(cancellationToken);
    }

    private async Task<Dictionary<string, string>> GetObservedStatesAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> replicaNames = await replicaProvider.GetReplicaNamesAsync();

        var states = await Task.WhenAll(replicaNames.Select(async replicaName =>
        {
            WorkItemProcessorState? state = await stateStore.GetObservedStateAsync(replicaName, cancellationToken);
            return (ReplicaName: replicaName, State: state ?? WorkItemProcessorState.Stopped);
        }));

        return states.ToDictionary(state => state.ReplicaName, state => state.State.ToString());
    }
}
