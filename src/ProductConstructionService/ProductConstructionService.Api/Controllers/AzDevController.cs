// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using Maestro.Common.AzureDevOpsTokens;
using Microsoft.AspNetCore.Mvc;

namespace ProductConstructionService.Api.Controllers;

[Route("[controller]")]
[Route("_/[controller]")]
public class AzDevController : ControllerBase
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

    public IAzureDevOpsTokenProvider TokenProvider { get; }

    public AzDevController(IAzureDevOpsTokenProvider tokenProvider)
    {
        TokenProvider = tokenProvider;
    }

    [HttpGet("build/status/{account}/{project}/{definitionId}/{*branch}")]
    public async Task<IActionResult> GetBuildStatus(string account, string project, int definitionId, string branch, int count, string status)
    {
        var token = await TokenProvider.GetTokenForAccountAsync(account);

        return await ProxyRequestAsync(
            HttpContext,
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

    private static async Task<IActionResult> ProxyRequestAsync(HttpContext context, HttpClient client, string targetUrl, Action<HttpRequestMessage> configureRequest)
    {
        using (var req = new HttpRequestMessage(HttpMethod.Get, targetUrl))
        {
            foreach (var (key, values) in context.Request.Headers)
            {
                switch (key.ToLower())
                {
                    // We shouldn't copy any of these request headers
                    case "host":
                    case "authorization":
                    case "cookie":
                    case "content-length":
                    case "content-type":
                        continue;
                    default:
                        try
                        {
                            req.Headers.Add(key, values.ToArray());
                        }
                        catch
                        {
                            // Some headers set by the client might be invalid (e.g. contain :)
                        }
                        break;
                }
            }

            configureRequest(req);

            HttpResponseMessage res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            context.Response.RegisterForDispose(res);

            foreach (var (key, values) in res.Headers)
            {
                switch (key.ToLower())
                {
                    // Remove headers that the response doesn't need
                    case "set-cookie":
                    case "x-powered-by":
                    case "x-aspnet-version":
                    case "server":
                    case "transfer-encoding":
                    case "access-control-expose-headers":
                    case "access-control-allow-origin":
                        continue;
                    default:
                        if (!context.Response.Headers.ContainsKey(key))
                        {
                            context.Response.Headers.Append(key, values.ToArray());
                        }

                        break;
                }
            }


            context.Response.StatusCode = (int)res.StatusCode;
            if (res.Content != null)
            {
                foreach (var (key, values) in res.Content.Headers)
                {
                    if (!context.Response.Headers.ContainsKey(key))
                    {
                        context.Response.Headers.Append(key, values.ToArray());
                    }
                }

                using (var data = await res.Content.ReadAsStreamAsync())
                {
                    await data.CopyToAsync(context.Response.Body);
                }
            }

            return new EmptyResult();
        }
    }
}
