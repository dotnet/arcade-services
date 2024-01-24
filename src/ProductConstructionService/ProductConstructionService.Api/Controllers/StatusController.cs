// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Extensions;
using ProductConstructionService.Api.Queue;

namespace ProductConstructionService.Api.Controllers;

[Route("status")]
public class PcsJobsProcessorStatusController(
    ILogger<PcsJobsProcessorStatusController> logger,
    PcsJobsProcessorStatus pcsJobsProcessorStatus,
    IHostApplicationLifetime hostApplicationLifetime) : Controller
{
    private readonly ILogger<PcsJobsProcessorStatusController> _logger = logger;
    private readonly PcsJobsProcessorStatus _pcsJobsProcessorStatus = pcsJobsProcessorStatus;
    private readonly IHostApplicationLifetime _hostApplicationLifetime = hostApplicationLifetime;

    private const int StoppedCheckDelaySeconds = 5;

    [HttpPut("stop")]
    public IActionResult StopPcsJobsProcessor()
    {
        _logger.LogInformation("Stopping {pcsJobsProcessor}. The currently running PcsJob will finish", nameof(PcsJobsProcessor));
        _pcsJobsProcessorStatus.State = PcsJobsProcessorState.FinishingJobAndStopping;

        return Ok();
    }

    [HttpPut("start")]
    public IActionResult StartPcsJobsProcessor()
    {
        _logger.LogInformation("Starting {pcsJobsProcessor}", nameof(PcsJobsProcessor));
        _pcsJobsProcessorStatus.Reset();

        return Ok();
    }

    [HttpGet]
    public IActionResult GetPcsJobsProcessorStatus()
    {
        return Ok(_pcsJobsProcessorStatus.State.GetDisplayName());
    }
}
