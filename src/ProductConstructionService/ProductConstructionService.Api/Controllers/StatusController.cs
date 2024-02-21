// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Extensions;
using ProductConstructionService.Api.Queue;

namespace ProductConstructionService.Api.Controllers;

[Route("status")]
public class StatusController(JobProcessorScopeManager jobProcessorScopeManager) : Controller
{
    private readonly JobProcessorScopeManager _jobProcessorScopeManager = jobProcessorScopeManager;

    [HttpPut("stop")]
    public IActionResult StopPcsJobProcessor()
    {
        _jobProcessorScopeManager.FinishJobAndStop();

        return GetPcsJobProcessorStatus();
    }

    [HttpPut("start")]
    public IActionResult StartPcsJobProcessor()
    {
        if (_jobProcessorScopeManager.State == JobsProcessorState.WaitingForVmrClone)
        {
            return BadRequest("The JobProcessor can't be started until the VMR is cloned");
        }

        _jobProcessorScopeManager.Start();

        return GetPcsJobProcessorStatus();
    }

    [HttpGet]
    public IActionResult GetPcsJobProcessorStatus()
    {
        return Ok(_jobProcessorScopeManager.State.GetDisplayName());
    }
}
