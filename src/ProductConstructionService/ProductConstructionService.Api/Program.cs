// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Rewrite;
using Microsoft.Extensions.FileProviders;
using ProductConstructionService.Api;
using ProductConstructionService.Api.Configuration;
using ProductConstructionService.Common;
using ProductConstructionService.ServiceDefaults;
using ProductConstructionService.WorkItems;

var builder = WebApplication.CreateBuilder(args);

bool isDevelopment = builder.Environment.IsDevelopment();
bool useSwagger = isDevelopment;

await builder.ConfigurePcs(
    addKeyVault: true,
    authRedis: !isDevelopment,
    addSwagger: useSwagger);

var app = builder.Build();

app.UseHttpsRedirection();

if (!isDevelopment)
{
    app.UseHsts();
}

app.UseCors();

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

// Configure the HTTP request pipeline.
if (isDevelopment)
{
    app.UseDeveloperExceptionPage();
    await app.Services.UseLocalWorkItemQueues(
        app.Configuration.GetRequiredValue(WorkItemConfiguration.WorkItemQueueNameConfigurationKey));

    if (useSwagger)
    {
        app.UseLocalSwagger();
    }
}

// When running locally, we need to add compiled WASM static files from the BarViz project
if (isDevelopment && Directory.Exists(PcsStartup.LocalCompiledStaticFilesPath))
{
    // When running locally, we need to add compiled WASM static files from the BarViz project
    var barVizFileProvider = new PhysicalFileProvider(PcsStartup.LocalCompiledStaticFilesPath);
    app.UseDefaultFiles(new DefaultFilesOptions()
    {
        FileProvider = barVizFileProvider,
    });

    app.UseStaticFiles(new StaticFileOptions()
    {
        ServeUnknownFileTypes = true,
        FileProvider = barVizFileProvider,
    });
}
else
{
    app.UseDefaultFiles();
    app.UseStaticFiles(new StaticFileOptions()
    {
        ServeUnknownFileTypes = true,
    });
}

app.UseCookiePolicy();
app.UseAuthentication();
app.UseRouting();
app.UseAuthorization();

// Map API controllers
app.MapWhen(
    ctx => ctx.Request.Path.StartsWithSegments("/api"),
    a => PcsStartup.ConfigureApi(a, isDevelopment));

// Add security headers
app.UseStatusCodePagesWithReExecute("/Error", "?code={0}");
app.ConfigureSecurityHeaders();

// Map pages and non-API controllers
app.MapDefaultEndpoints();
app.MapRazorPages();
var controllers = app.MapControllers();
if (isDevelopment)
{
    controllers.AllowAnonymous();
}

// Redirect all GET requests to the index page (Angular SPA)
app.MapWhen(PcsStartup.IsGet, a =>
{
    a.UseRewriter(new RewriteOptions().AddRewrite(".*", "/index.html", true));
    a.UseAuthentication();
    a.UseRouting();
    a.UseAuthorization();
    a.UseEndpoints(e => e.MapRazorPages());
});

app.Run();
