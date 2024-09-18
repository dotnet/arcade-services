// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.DarcLib;
using ProductConstructionService.Api.Controllers.Models;

namespace ProductConstructionService.Api.Controllers;

[Route("azdo")]
[ApiVersion("2020-02-20")]
public class AzDoController(IAzureDevOpsClient azureDevOpsClient) : ControllerBase
{
    private readonly IAzureDevOpsClient _azureDevOpsClient = azureDevOpsClient;

    private const string ValueKey = "value";

    [HttpGet("build/status/{account}/{project}/{definitionId}/{*branch}")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(List<AzDoBuild>), Description = "The latest Build matching the search criteria")]
    public async Task<IActionResult> GetBuildStatus(string account, string project, int definitionId, string? branch, int count, string status)
    {
        var jsonResponse = await _azureDevOpsClient.GetBuildsAsync(account, project, definitionId, branch, count, status);
        if (!jsonResponse.ContainsKey(ValueKey))
        {
            return NotFound();
        }
        return Ok(jsonResponse[ValueKey]!.ToObject<List<AzDoBuild>>());
    }
}
