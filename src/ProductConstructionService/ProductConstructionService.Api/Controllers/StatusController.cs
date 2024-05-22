// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Extensions;
using ProductConstructionService.Api.Controllers.ActionResults;
using ProductConstructionService.Api.Queue;
using ProductConstructionService.Api.Queue.Jobs;

namespace ProductConstructionService.Api.Controllers;

[Route("status")]
internal class StatusController(JobScopeManager jobProcessorScopeManager, JobProducerFactory jobProducerFactory)
    : InternalController
{
    private readonly JobScopeManager _jobProcessorScopeManager = jobProcessorScopeManager;
    private readonly JobProducerFactory _jobProducerFactory = jobProducerFactory;

    [HttpPut("stop", Name = "Stop")]
    public async Task<IActionResult> StopPcsJobProcessor()
    {
        _jobProcessorScopeManager.FinishJobAndStop();

        return await GetPcsJobProcessorStatus();
    }

    [HttpPut("start", Name = "Start")]
    public async Task<IActionResult> StartPcsJobProcessor()
    {
        if (_jobProcessorScopeManager.State == JobsProcessorState.Initializing)
        {
            return new PreconditionFailedActionResult("The background worker can't be started until the VMR is cloned");
        }

        _jobProcessorScopeManager.Start();

        return await GetPcsJobProcessorStatus();
    }

    [AllowAnonymous]
    [HttpGet(Name = "Status")]
    public async Task<IActionResult> GetPcsJobProcessorStatus()
    {
        await _jobProducerFactory.Create<TextJob>().ProduceJobAsync(new()
        {
            Text = "Status requested"
        });
        return Ok(_jobProcessorScopeManager.State.GetDisplayName());
    }
}
