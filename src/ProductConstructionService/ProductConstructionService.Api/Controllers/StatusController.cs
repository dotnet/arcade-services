// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.Api.Controllers;

[Route("status")]
[ApiVersion("2020-02-20")]
public class StatusController : ControllerBase
{
    private readonly IReplicaWorkItemProcessorStateWriterFactory _replicaWorkItemProcessorStateFactory;

    public StatusController(IReplicaWorkItemProcessorStateWriterFactory replicaWorkItemProcessorStateFactory) => _replicaWorkItemProcessorStateFactory = replicaWorkItemProcessorStateFactory;

    [AllowAnonymous]
    [HttpGet(Name = "Status")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(Dictionary<string, string>), Description = "Returns PCS replica states")]
    public async Task<IActionResult> GetPcsWorkItemProcessorStatus()
    {
        var stateTasks = (await _replicaWorkItemProcessorStateFactory.GetAllWorkItemProcessorStatesAsync())
            .Select(async (processorState) =>
            {
                var state = await processorState.GetStateAsync();
                return (processorState.ReplicaName, state);
            }).ToArray();

        Task.WaitAll(stateTasks);

        var ret = stateTasks.Select(task => task.Result)
            .ToDictionary(res => res.ReplicaName, res => res.state);

        return Ok(ret);
    }

    [HttpPut("start", Name = "Start")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(Dictionary<string, string>), Description = "Starts all PCS replicas")]
    public async Task<IActionResult> StartPcsWorkItemProcessors()
    {
        var startTasks = (await _replicaWorkItemProcessorStateFactory.GetAllWorkItemProcessorStatesAsync())
            .Select(async state => await state.SetStartAsync())
            .ToArray();

        Task.WaitAll(startTasks);

        return await GetPcsWorkItemProcessorStatus();
    }

    [HttpPut("stop", Name = "Stop")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(Dictionary<string, string>), Description = "Tells all PCS replicas to stop after finishing their current work item")]
    public async Task<IActionResult> StopPcsWorkItemProcessors()
    {
        var stopTasks = (await _replicaWorkItemProcessorStateFactory.GetAllWorkItemProcessorStatesAsync())
            .Select(async state => await state.FinishWorkItemAndStopAsync())
            .ToArray();

        Task.WaitAll(stopTasks);

        return await GetPcsWorkItemProcessorStatus();
    }
}
