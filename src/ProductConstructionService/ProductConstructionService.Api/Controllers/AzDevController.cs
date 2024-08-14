// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using Maestro.Common.AzureDevOpsTokens;
using Microsoft.AspNetCore.Mvc;
using ProductConstructionService.Api.Configuration;

namespace ProductConstructionService.Api.Controllers;

[Route("[controller]")]
[Route("_/[controller]")]
public class AzDevController(IAzureDevOpsTokenProvider tokenProvider)
    : ControllerBase
{
    private static readonly Lazy<HttpClient> s_lazyClient = new(CreateHttpClient);
    private static HttpClient CreateHttpClient() =>
        new(new HttpClientHandler { CheckCertificateRevocationList = true })
        {
            DefaultRequestHeaders =
            {
                UserAgent =
                {
                    new ProductInfoHeaderValue("MaestroApi", GetApplicationVersion())
                }
            }
        };

    public IAzureDevOpsTokenProvider TokenProvider { get; } = tokenProvider;

    [HttpGet("build/status/{account}/{project}/{definitionId}/{*branch}")]
    public async Task<IActionResult> GetBuildStatus(string account, string project, int definitionId, string branch, int count, string status)
    {
        var token = await TokenProvider.GetTokenForAccountAsync(account);

        return await HttpContext.ProxyRequestAsync(
            s_lazyClient.Value,
            $"https://dev.azure.com/{account}/{project}/_apis/build/builds?api-version=5.0&definitions={definitionId}&branchName={branch}&statusFilter={status}&$top={count}",
            req =>
            {
                req.Headers.Authorization = new AuthenticationHeaderValue(
                    "Basic",
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(":" + token)));
            });
    }

    private static string GetApplicationVersion()
    {
        Assembly assembly = typeof(AzDevController).Assembly;
        var infoVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        if (infoVersion != null)
        {
            return infoVersion.InformationalVersion;
        }

        var version = assembly.GetCustomAttribute<AssemblyVersionAttribute>();
        if (version != null)
        {
            return version.Version;
        }

        return "42.42.42.42";
    }
}
