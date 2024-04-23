﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Mvc;

namespace ProductConstructionService.Api.Controllers.ActionResults;

public class PreconditionFailedActionResult(string message) : ActionResult
{
    public override async Task ExecuteResultAsync(ActionContext context)
    {
        ObjectResult objectResult = new(message)
        {
            StatusCode = StatusCodes.Status412PreconditionFailed
        };

        await objectResult.ExecuteResultAsync(context);
    }
}
