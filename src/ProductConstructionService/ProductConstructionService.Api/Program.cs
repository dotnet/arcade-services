// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Storage.Queues;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.Extensions.FileProviders;
using ProductConstructionService.Api;
using ProductConstructionService.Api.Configuration;
using ProductConstructionService.Api.Queue;
using ProductConstructionService.Common;

var builder = WebApplication.CreateBuilder(args);

bool isDevelopment = builder.Environment.IsDevelopment();
bool useSwagger = isDevelopment;

await builder.ConfigurePcs(
    addKeyVault: true,
    isDevelopment: isDevelopment,
    addSwagger: useSwagger);

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

// Configure the HTTP request pipeline.
if (isDevelopment)
{
    app.UseDeveloperExceptionPage();

    // When running locally, we need to add compiled static files from the maestro-angular project as they are not published
    app.UseStaticFiles(new StaticFileOptions()
    {
        FileProvider = new CompositeFileProvider(
            new PhysicalFileProvider(PcsStartup.LocalCompiledStaticFilesPath),
            new PhysicalFileProvider(Path.Combine(Environment.CurrentDirectory, "wwwroot"))),
    });

    // When running locally, create the workitem queue, if it doesn't already exist
    var queueServiceClient = app.Services.GetRequiredService<QueueServiceClient>();
    var queueClient = queueServiceClient.GetQueueClient(app.Configuration.GetRequiredValue(QueueConfiguration.JobQueueNameConfigurationKey));
    await queueClient.CreateIfNotExistsAsync();

    if (useSwagger)
    {
        app.UseLocalSwagger();
    }
}
else
{
    app.UseStaticFiles();
}

app.UseStatusCodePagesWithReExecute("/Error", "?code={0}");
app.UseCookiePolicy();
app.UseAuthentication();
app.UseRouting();
app.UseAuthorization();

// Map API controllers
app.MapWhen(
    ctx => ctx.Request.Path.StartsWithSegments("/api"),
    a => PcsStartup.ConfigureApi(a, isDevelopment));

// Add security headers
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
    a.UseRewriter(new RewriteOptions().AddRewrite(".*", "Index", true));
    a.UseAuthentication();
    a.UseRouting();
    a.UseAuthorization();
    a.UseEndpoints(e => e.MapRazorPages());
});

app.Run();
