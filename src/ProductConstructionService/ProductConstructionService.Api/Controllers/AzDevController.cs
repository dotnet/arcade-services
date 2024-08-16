// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using Maestro.Common.AzureDevOpsTokens;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.DarcLib;
using ProductConstructionService.Api.Configuration;

namespace ProductConstructionService.Api.Controllers;

[Route("[controller]")]
[Route("_/[controller]")]
public class AzDevController(IAzureDevOpsClient azureDevOpsClient)
    : ControllerBase
{
    private static readonly Lazy<HttpClient> s_lazyClient = new(() =>
        new(new HttpClientHandler { CheckCertificateRevocationList = true })
        {
            DefaultRequestHeaders =
            {
                UserAgent =
                {
                    new ProductInfoHeaderValue("MaestroApi", GetApplicationVersion())
                }
            }
        });

    private readonly IAzureDevOpsClient _azureDevOpsClient = azureDevOpsClient;

    [HttpGet("build/status/{account}/{project}/{definitionId}/{*branch}")]
    public async Task<IActionResult> GetBuildStatus(string account, string project, int definitionId, string branch, int count, string status)
    {
        return Ok(await _azureDevOpsClient.GetBuildsAsync(account, project, definitionId, branch, count, status));
    }

    private static string GetApplicationVersion()
    {
        Assembly assembly = typeof(AzDevController).Assembly;
        var infoVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        if (infoVersion != null)
        {
            return infoVersion.InformationalVersion;
        }

        return assembly.GetCustomAttribute<AssemblyVersionAttribute>()?.Version ?? "42.42.42.42";
    }
}
