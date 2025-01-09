// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProductConstructionService.Api.Configuration;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.Api.Controllers;

[Route("status")]
[ApiVersion("2020-02-20")]
[Authorize(Policy = AuthenticationConfiguration.AdminAuthorizationPolicyName)]
public class StatusController(IReplicaWorkItemProcessorStateCacheFactory replicaWorkItemProcessorStateCacheFactory) : ControllerBase
{
    private readonly IReplicaWorkItemProcessorStateCacheFactory _replicaWorkItemProcessorStateCacheFactory = replicaWorkItemProcessorStateCacheFactory;

    [AllowAnonymous]
    [HttpGet(Name = "Status")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(Dictionary<string, string>), Description = "Returns PCS replica states")]
    public async Task<IActionResult> GetPcsWorkItemProcessorStatus()
    {
        return Ok(await PerformActionOnAllProcessors(async stateCache =>
        {
            var state = await stateCache.GetStateAsync();
            return (stateCache.ReplicaName, state ?? WorkItemProcessorState.Stopped);
        }));
    }

    [HttpPut("start", Name = "Start")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(Dictionary<string, string>), Description = "Starts all PCS replicas")]
    public async Task<IActionResult> StartPcsWorkItemProcessors()
    {
        return Ok(await PerformActionOnAllProcessors(async stateCache =>
        {
            await stateCache.SetStateAsync(WorkItemProcessorState.Working);
            return (stateCache.ReplicaName, WorkItemProcessorState.Working);
        }));
    }

    [HttpPut("stop", Name = "Stop")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(Dictionary<string, string>), Description = "Tells all PCS replicas to stop after finishing their current work item")]
    public async Task<IActionResult> StopPcsWorkItemProcessors()
    {
        return Ok(await PerformActionOnAllProcessors(async stateCache =>
        {
            var state = await stateCache.GetStateAsync();
            switch (state)
            {
                case WorkItemProcessorState.Stopping:
                case WorkItemProcessorState.Working:
                    await stateCache.SetStateAsync(WorkItemProcessorState.Stopping);
                    return (stateCache.ReplicaName, WorkItemProcessorState.Stopping);
                case WorkItemProcessorState.Initializing:
                    throw new BadHttpRequestException("Can't stop the service while initializing, try again later");
                case WorkItemProcessorState.Stopped:
                    return (stateCache.ReplicaName, WorkItemProcessorState.Stopped);
                default:
                    throw new Exception("PCS is in an unsupported state");
            }
        }));
    }

    private async Task<Dictionary<string, string>> PerformActionOnAllProcessors(Func<WorkItemProcessorStateCache, Task<(string replicaName, string state)>> action)
    {
        var tasks = (await _replicaWorkItemProcessorStateCacheFactory.GetAllWorkItemProcessorStateCachesAsync())
            .Select(async processorStateCache => await action(processorStateCache))
            .ToArray();

        await Task.WhenAll(tasks);

        return tasks.Select(task => task.Result)
            .ToDictionary(res => res.replicaName, res => res.state);
    }
}
