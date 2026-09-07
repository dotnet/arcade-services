// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Common;
using Maestro.Services.Common;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using ProductConstructionService.Api;
using ProductConstructionService.Api.Configuration;
using ProductConstructionService.BarViz.Hosting;
using Maestro.WorkItems;

var builder = WebApplication.CreateBuilder(args);

bool isDevelopment = builder.Environment.IsDevelopment();
bool useSwagger = isDevelopment;

await builder.ConfigurePcs(
    addKeyVault: true,
    addSwagger: useSwagger);
builder.Services.AddBarVizHosting();
builder.ConfigureApiRedirection();

var app = builder.Build();

app.UseHttpsRedirection();

if (!isDevelopment)
{
    app.UseHsts();
}

// When we're using GitHub authentication on BarViz, one of the parameters that we're giving GitHub is the redirect_uri
// When we authenticate ourselves, GitHub sends us the token, and redirects us to the redirect_uri so this needs to be on HTTPS
// When using Application Gateway with TLS termination, we, the Client, talk to the Gateway over HTTPS,
// the Gateway then transforms that package to HTTP, and the communication between the Gateway and
// server is done over HTTP. Because of this, the Aspnet library that's handling the authentication is giving GitHub the
// http uri, for the redirect_uri parameter.
// The code below fixes that by adding middleware that will make it so the asp library thinks the call was made over HTTPS
// so it will set the redirect_uri to https too
app.Use((context, next) =>
{
    context.Request.Scheme = "https";
    return next(context);
});

app.UseHttpLogging();

if (isDevelopment)
{
    app.UseDeveloperExceptionPage();
    app.UseWebAssemblyDebugging();
    await app.Services.UseLocalWorkItemQueues(
        [
            app.Configuration.GetRequiredValue(PcsStartup.ConfigurationKeys.DefaultWorkItemQueueName),
            app.Configuration.GetRequiredValue(PcsStartup.ConfigurationKeys.CodeFlowWorkItemQueueName),
        ]);

    if (useSwagger)
    {
        app.UseLocalSwagger();
    }
}

app.UseCookiePolicy();
app.UseAuthentication();
app.UseRouting();
app.UseAuthorization();

// Map API controllers
app.MapWhen(
    ctx => ctx.Request.Path.StartsWithSegments("/api"),
    a => PcsStartup.ConfigureApi(a, isDevelopment));

app.Use(async (context, next) =>
{
    if (!await context.IsAuthenticated())
    {
        await context.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme);
    }

    await next(context);
});

// Add security headers
app.ConfigureSecurityHeaders();
app.UseAntiforgery();

// Map pages and non-API controllers
app.MapDefaultEndpoints();
app.MapRazorPages();

var controllers = app.MapControllers();
if (isDevelopment)
{
    controllers.AllowAnonymous();
}

app.MapBarViz(AuthenticationConfiguration.WebAuthorizationPolicyName);

await app.SetWorkItemProcessorInitialState();

app.Run();
