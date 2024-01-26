// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Extensions;
using ProductConstructionService.Api.Queue;

namespace ProductConstructionService.Api.Controllers;

[Route("status")]
public class StatusController(JobsProcessorStatus pcsJobsProcessorStatus) : Controller
{
    private readonly JobsProcessorStatus _pcsJobsProcessorStatus = pcsJobsProcessorStatus;

    [HttpPut("stop")]
    public IActionResult StopPcsJobsProcessor()
    {
        _pcsJobsProcessorStatus.FinishJobAndStop();

        return GetPcsJobsProcessorStatus();
    }

    [HttpPut("start")]
    public IActionResult StartPcsJobsProcessor()
    {
        _pcsJobsProcessorStatus.Reset();

        return GetPcsJobsProcessorStatus();
    }

    [HttpGet]
    public IActionResult GetPcsJobsProcessorStatus()
    {
        return Ok(_pcsJobsProcessorStatus.State.GetDisplayName());
    }
}
