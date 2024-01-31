// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Extensions;
using ProductConstructionService.Api.Queue;

namespace ProductConstructionService.Api.Controllers;

[Route("status")]
public class StatusController(JobsProcessorScopeManager jobsProcessorScopeManager) : Controller
{
    private readonly JobsProcessorScopeManager _jobsProcessorScopeManager = jobsProcessorScopeManager;

    [HttpPut("stop")]
    public IActionResult StopPcsJobsProcessor()
    {
        _jobsProcessorScopeManager.FinishJobAndStop();

        return GetPcsJobsProcessorStatus();
    }

    [HttpPut("start")]
    public IActionResult StartPcsJobsProcessor()
    {
        _jobsProcessorScopeManager.Start();

        return GetPcsJobsProcessorStatus();
    }

    [HttpGet]
    public IActionResult GetPcsJobsProcessorStatus()
    {
        return Ok(_jobsProcessorScopeManager.State.GetDisplayName());
    }
}
