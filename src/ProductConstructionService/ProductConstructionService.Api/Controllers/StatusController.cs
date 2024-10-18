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
    private readonly IReplicaWorkItemProcessorStateWriterFactory _replicaWorkItemProcessorStateWriterFactory;

    public StatusController(IReplicaWorkItemProcessorStateWriterFactory replicaWorkItemProcessorStateWriterFactory) => _replicaWorkItemProcessorStateWriterFactory = replicaWorkItemProcessorStateWriterFactory;

    [AllowAnonymous]
    [HttpGet(Name = "Status")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(Dictionary<string, string>), Description = "Returns PCS replica states")]
    public async Task<IActionResult> GetPcsWorkItemProcessorStatus()
    {
        return Ok(await PerformActionOnAllProcessors(async stateWriter =>
        {
            var state = await stateWriter.GetStateAsync();
            return (stateWriter.ReplicaName, state ?? WorkItemProcessorState.Stopped);
        }));
    }

    [HttpPut("start", Name = "Start")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(Dictionary<string, string>), Description = "Starts all PCS replicas")]
    public async Task<IActionResult> StartPcsWorkItemProcessors()
    {
        return Ok(await PerformActionOnAllProcessors(async stateWriter =>
        {
            await stateWriter.SetStateAsync(WorkItemProcessorState.Working);
            return (stateWriter.ReplicaName, WorkItemProcessorState.Working);
        }));
    }

    [HttpPut("stop", Name = "Stop")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(Dictionary<string, string>), Description = "Tells all PCS replicas to stop after finishing their current work item")]
    public async Task<IActionResult> StopPcsWorkItemProcessors()
    {
        return Ok(await PerformActionOnAllProcessors(async stateWriter =>
        {
            await stateWriter.SetStateAsync(WorkItemProcessorState.Stopping);
            return (stateWriter.ReplicaName, WorkItemProcessorState.Stopping);
        }));
    }

    private async Task<Dictionary<string, string>> PerformActionOnAllProcessors(Func<WorkItemProcessorStateWriter, Task<(string replicaName, string state)>> action)
    {
        var tasks = (await _replicaWorkItemProcessorStateWriterFactory.GetAllWorkItemProcessorStateWritersAsync())
            .Select(async processorState => await action(processorState))
            .ToArray();

        await Task.WhenAll(tasks);

        return tasks.Select(task => task.Result)
            .ToDictionary(res => res.replicaName, res => res.state);
    }
}
