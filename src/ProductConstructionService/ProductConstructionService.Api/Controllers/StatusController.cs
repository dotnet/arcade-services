// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Extensions;
using ProductConstructionService.Api.Controllers.ActionResults;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.Api.Controllers;

[Route("status")]
[ApiVersion("2020-02-20")]
public class StatusController(WorkItemScopeManager workItemScopeManager)
    : ControllerBase
{
    [HttpPut("stop", Name = "Stop")]
    public IActionResult StopPcsWorkItemProcessor()
    {
        workItemScopeManager.FinishWorkItemAndStop();
        return GetPcsWorkItemProcessorStatus();
    }

    [HttpPut("start", Name = "Start")]
    public IActionResult StartPcsWorkItemProcessor()
    {
        if (workItemScopeManager.State == WorkItemProcessorState.Initializing)
        {
            return new PreconditionFailedActionResult("The background worker can't be started until the VMR is cloned");
        }

        workItemScopeManager.Start();

        return GetPcsWorkItemProcessorStatus();
    }

    [AllowAnonymous]
    [HttpGet(Name = "Status")]
    public IActionResult GetPcsWorkItemProcessorStatus()
    {
        return Ok(workItemScopeManager.State.GetDisplayName());
    }
}
