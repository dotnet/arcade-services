// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.DarcLib;

namespace ProductConstructionService.Api.Api.v2020_02_20.Controllers;

[Route("azdo")]
[ApiVersion("2020-02-20")]
public class AzDoController(IAzureDevOpsClient azureDevOpsClient) : ControllerBase
{
    private readonly IAzureDevOpsClient _azureDevOpsClient = azureDevOpsClient;

    [HttpGet("build/status/{a}")]
    public async Task<IActionResult> GetBuildStatus(string a)
    {
        await Task.Delay(50);
        return Ok(a);
        //return Ok(await _azureDevOpsClient.GetBuildsAsync(account, project, definitionId, branch, count, status));
    }
}
