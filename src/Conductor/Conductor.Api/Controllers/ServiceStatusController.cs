// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Mvc;

namespace Conductor.Api.Controllers;

[ApiController]
[Route("status")]
public class ServiceStatusController : Controller
{
    [HttpGet("startup")]
    public IActionResult GetStartupStatus()
    {
        return BadRequest();
    }
}
