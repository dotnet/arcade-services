// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Mvc;

namespace ProductConstructionService.Api.Controllers;

[ApiController]
[Route("status")]
public class ServiceStatusController : Controller
{
    private ILogger<ServiceStatusController> _logger;

    public ServiceStatusController(ILogger<ServiceStatusController> logger)
    {
        _logger = logger;
    }

    [HttpGet("startup")]
    public IActionResult GetStartupStatus()
    {
        _logger.LogInformation("asd");
        return Ok();
    }
}
