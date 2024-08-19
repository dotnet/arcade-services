// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using System.Net.Http.Headers;
using Azure.Core;
using Maestro.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.Maestro.Client;

namespace ProductConstructionService.Api.Configuration;

internal static class ApiRedirection
{
    private const string ApiRedirectionConfiguration = "ApiRedirect";
    private const string ApiRedirectionTarget = "Uri";
    private const string ApiRedirectionToken = "Token";
    private const string ManagedIdentityId = "ManagedIdentityClientId";

    public static void ConfigureApiRedirection(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration.GetSection(ApiRedirectionConfiguration);
        if (!config.Exists())
        {
            return;
        }

        string? apiRedirectionTarget = config[ApiRedirectionTarget];
        if (string.IsNullOrEmpty(apiRedirectionTarget))
        {
            return;
        }

        string? token = config[ApiRedirectionToken];

        var managedIdentityId = config[ManagedIdentityId];
        var maestroClient = MaestroApiFactory.GetAuthenticated(
            apiRedirectionTarget,
            accessToken: token,
            managedIdentityId: managedIdentityId,
            disableInteractiveAuth: !builder.Environment.IsDevelopment());

        builder.Services.AddKeyedSingleton<IMaestroApi>(apiRedirectionTarget, maestroClient);
    }

    public static void UseApiRedirection(this IApplicationBuilder app)
    {
        var apiRedirectionTarget = app.ApplicationServices
            .GetRequiredService<IConfiguration>()
            .GetSection(ApiRedirectionConfiguration)[ApiRedirectionTarget];

        if (apiRedirectionTarget == null)
        {
            return;
        }

        static bool ShouldRedirect(HttpContext ctx)
        {
            return ctx.IsGet()
                && ctx.Request.Path.StartsWithSegments("/api")
                && !ctx.Request.Cookies.TryGetValue("Skip-Api-Redirect", out _);
        }

        app.MapWhen(ShouldRedirect, a => a.Run(b => RedirectionHandler(b, apiRedirectionTarget)));
    }

    private static async Task<IActionResult> ProxyRequestAsync(this HttpContext context, HttpClient client, string targetUrl, Action<HttpRequestMessage> configureRequest)
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

    public static async Task RedirectionHandler(HttpContext ctx, string apiRedirectionTarget)
    {
        var logger = ctx.RequestServices.GetRequiredService<ILogger<IApplicationBuilder>>();
        logger.LogDebug("Preparing for redirect to '{redirectUrl}'", apiRedirectionTarget);

        var authTasks = AuthenticationConfiguration.AuthenticationSchemes.Select(ctx.AuthenticateAsync);
        var authResults = await Task.WhenAll(authTasks);
        var success = authResults.FirstOrDefault(t => t.Succeeded);

        if (ctx.User == null || success == null)
        {
            logger.LogInformation("Rejecting redirect because of missing authentication");
            ctx.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            return;
        }

        var authService = ctx.RequestServices.GetRequiredService<IAuthorizationService>();
        AuthorizationResult result = await authService.AuthorizeAsync(success.Ticket!.Principal, AuthenticationConfiguration.MsftAuthorizationPolicyName);
        if (!result.Succeeded)
        {
            logger.LogInformation("Rejecting redirect because authorization failed");
            ctx.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            return;
        }

        using (var client = new HttpClient(new HttpClientHandler { CheckCertificateRevocationList = true }))
        {
            logger.LogInformation("Preparing proxy request to {proxyPath}", ctx.Request.Path);
            var uri = new UriBuilder(apiRedirectionTarget)
            {
                Path = ctx.Request.Path,
                Query = ctx.Request.QueryString.ToUriComponent(),
            };

            string absoluteUri = uri.Uri.AbsoluteUri;
            logger.LogInformation("Service proxied request to {url}", absoluteUri);
            await ctx.ProxyRequestAsync(client, absoluteUri,
                async req =>
                {
                    var maestroApi = ctx.RequestServices.GetRequiredKeyedService<IMaestroApi>(apiRedirectionTarget);
                    AccessToken token = await maestroApi.Options.Credentials.GetTokenAsync(new(), CancellationToken.None);
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
                });
        }
    }
}
