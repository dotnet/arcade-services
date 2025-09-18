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
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(bool), Description = "Refresh subscription configuration from a given source")]
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

    [HttpDelete(Name = "delete")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(bool), Description = "Delete in subscription configuration from a given source")]
    [SwaggerApiResponse(HttpStatusCode.BadRequest, Type = typeof(ApiError), Description = "Cannot delete configuration for the specified branch")]
    public async Task<IActionResult> ClearConfiguration(string repoUri, string branch)
    {
        if (string.IsNullOrEmpty(repoUri) || branch == "staging" || branch == "production")
        {
            return BadRequest(new ApiError("Cannot delete configuration for the specified branch"));
        }

        try
        {
            await configurationDataIngestor.ClearConfiguration(repoUri, branch);
            return Ok(true);
        }
        catch (Exception e)
        {
            return BadRequest(new ApiError(e.Message));
        }
    }
}
