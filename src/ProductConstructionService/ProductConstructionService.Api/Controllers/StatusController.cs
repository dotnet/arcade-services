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
    public IActionResult StopPcsJobProcessor()
    {
        _jobProcessorScopeManager.FinishJobAndStop();

        return GetPcsJobProcessorStatus().Result;
    }

    [HttpPut("start", Name = "Start")]
    public IActionResult StartPcsJobProcessor()
    {
        if (_jobProcessorScopeManager.State == JobsProcessorState.Initializing)
        {
            return new PreconditionFailedActionResult("The background worker can't be started until the VMR is cloned");
        }

        _jobProcessorScopeManager.Start();

        return GetPcsJobProcessorStatus().Result;
    }

    [AllowAnonymous]
    [HttpGet(Name = "Status")]
    public async Task<IActionResult> GetPcsJobProcessorStatus()
    {
        var query = new KustoQuery("Jobs | take 1");
        var reader = await kusto.ExecuteKustoQueryAsync(query);
        reader.Read();
        return Ok($"{reader.GetString(0)} {reader.GetString(1)}");
    }
}
