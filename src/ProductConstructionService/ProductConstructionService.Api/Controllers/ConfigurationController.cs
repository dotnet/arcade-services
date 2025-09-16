// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using Maestro.DataProviders;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProductConstructionService.Api.Api;
using ProductConstructionService.Api.Configuration;

namespace ProductConstructionService.Api.Controllers;

[Route("configuration")]
[ApiVersion("2020-02-20")]
[Authorize(Policy = AuthenticationConfiguration.AdminAuthorizationPolicyName)]
public class ConfigurationController(IConfigurationDataIngestor configurationDataIngestor)
    : Controller
{
    [HttpGet(Name = "refresh")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(bool), Description = "Makes PCS refresh in memory subscription configuration")]
    public async Task<IActionResult> RefreshConfiguration(string repoUri, string branch)
    {
        try
        {
            await configurationDataIngestor.IngestConfiguration(repoUri, branch);
            return Ok(true);
        }
        catch (Exception e)
        {
            return BadRequest(new ApiError(e.Message));
        }
    }
}
