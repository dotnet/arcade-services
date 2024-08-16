// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.DarcLib;

namespace ProductConstructionService.Api.Controllers;

[Route("[controller]")]
[Route("_/[controller]")]
public class AzDevController(IAzureDevOpsClient azureDevOpsClient)
    : ControllerBase
{
    private readonly IAzureDevOpsClient _azureDevOpsClient = azureDevOpsClient;

    [HttpGet("build/status/{account}/{project}/{definitionId}/{*branch}")]
    public async Task<IActionResult> GetBuildStatus(string account, string project, int definitionId, string? branch, int count, string status)
    {
        return Ok(await _azureDevOpsClient.GetBuildsAsync(account, project, definitionId, branch, count, status));
    }
}
