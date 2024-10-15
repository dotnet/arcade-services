// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.ResourceManager.AppContainers;
using Microsoft.AspNetCore.ApiVersioning;
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
    public async IActionResult GetPcsWorkItemProcessorStatus()
    {
        var activeRevisionTrafficWeight = _containerApp.Data.Configuration.Ingress.Traffic
            .Single(traffic => traffic.Weight == 100);

        if (string.IsNullOrEmpty(activeRevisionTrafficWeight.RevisionName))
        {
            return StatusCode(500, "Internal server error");
        }

        List<WorkItemProcessorState> processorStates = await _containerApp.GetRevisionReplicaStatesAsync(
            activeRevisionTrafficWeight.RevisionName,
            _redisCacheFactory,
            _serviceProvider);

        var stateTasks = processorStates.Select(async (processorState) =>
        {
            var state = await processorState.GetStateAsync();
            return (processorState.ReplicaName, state);
        }).ToArray();

        Task.WaitAll(stateTasks);

        var ret = stateTasks.Select(task => task.Result)
            .ToDictionary(res => res.ReplicaName, res => res.state);
    }
}
