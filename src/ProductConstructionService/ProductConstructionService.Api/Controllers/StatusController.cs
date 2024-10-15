// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using Azure.ResourceManager.AppContainers;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Mvc;
using ProductConstructionService.Common;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.Api.Controllers;

[Route("status")]
[ApiVersion("2020-02-20")]
public class StatusController : ControllerBase
{
    private readonly IRedisCacheFactory _redisCacheFactory;
    private readonly ContainerAppResource _containerApp;
    private readonly IServiceProvider _serviceProvider;

    public StatusController(IRedisCacheFactory redisCacheFactory, ContainerAppResource containerApp, IServiceProvider serviceProvider)
    {
        _redisCacheFactory = redisCacheFactory;
        _containerApp = containerApp;
        _serviceProvider = serviceProvider;
    }

    [HttpGet(Name = "Status")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(Dictionary<string, string>), Description = "PCS replica states")]
    public async Task<IActionResult> GetPcsWorkItemProcessorStatus()
    {
        List<WorkItemProcessorState> processorStates = await GetWorkItemProcessorStatesAsync();

        var stateTasks = processorStates.Select(async (processorState) =>
        {
            var state = await processorState.GetStateAsync();
            return (processorState.ReplicaName, state);
        }).ToArray();

        Task.WaitAll(stateTasks);

        var ret = stateTasks.Select(task => task.Result)
            .ToDictionary(res => res.ReplicaName, res => res.state);

        return Ok(ret);
    }

    private async Task<List<WorkItemProcessorState>> GetWorkItemProcessorStatesAsync()
    {
        var activeRevisionTrafficWeight = _containerApp.Data.Configuration.Ingress.Traffic
            .Single(traffic => traffic.Weight == 100);

        if (string.IsNullOrEmpty (activeRevisionTrafficWeight.RevisionName))
        {
            throw new Exception("Internal server error");
        }

        return await _containerApp.GetRevisionReplicaStatesAsync(
            activeRevisionTrafficWeight.RevisionName,
            _redisCacheFactory,
            _serviceProvider);
    }
}
