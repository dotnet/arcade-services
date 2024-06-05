// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.Kusto;
using Microsoft.OpenApi.Extensions;
using ProductConstructionService.Api.Controllers.ActionResults;
using ProductConstructionService.Api.Queue;

namespace ProductConstructionService.Api.Controllers;

[Route("status")]
internal class StatusController(JobScopeManager jobProcessorScopeManager, IKustoClientProvider kusto)
    : InternalController
{
    private readonly JobScopeManager _jobProcessorScopeManager = jobProcessorScopeManager;

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
        var query = new KustoQuery("TimelineBuilds | take 1");
        var result = await kusto.ExecuteKustoQueryAsync(query);
        result.Read();
        return Ok(result.GetString(1));
    }
}
