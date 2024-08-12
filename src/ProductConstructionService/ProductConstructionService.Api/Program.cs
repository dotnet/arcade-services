// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Identity;
using Azure.Storage.Queues;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.Extensions.FileProviders;
using ProductConstructionService.Api.Configuration;
using ProductConstructionService.Api.Queue;
using ProductConstructionService.Api.VirtualMonoRepo;

var builder = WebApplication.CreateBuilder(args);

string vmrPath = builder.Configuration.GetRequiredValue(VmrConfiguration.VmrPathKey);
string tmpPath = builder.Configuration.GetRequiredValue(VmrConfiguration.TmpPathKey);
string vmrUri = builder.Configuration.GetRequiredValue(VmrConfiguration.VmrUriKey);

DefaultAzureCredential credential = new(new DefaultAzureCredentialOptions
{
    ManagedIdentityClientId = builder.Configuration[PcsStartup.ManagedIdentityId]
});

bool isDevelopment = builder.Environment.IsDevelopment();
var apiRedirection = builder.Configuration.GetSection(PcsStartup.ApiRedirectionConfiguration);
bool apiRedirectionEnabled = apiRedirection.Exists();
string? apiRedirectionTarget = apiRedirectionEnabled
    ? apiRedirection[PcsStartup.ApiRedirectionTarget] ?? throw new Exception($"{PcsStartup.ApiRedirectionConfiguration}:{PcsStartup.ApiRedirectionTarget} is missing")
    : null;

bool useSwagger = isDevelopment;

builder.ConfigurePcs(
    vmrPath: vmrPath,
    tmpPath: tmpPath,
    vmrUri: vmrUri,
    azureCredential: credential,
    keyVaultUri: new Uri($"https://{builder.Configuration.GetRequiredValue(PcsStartup.KeyVaultName)}.vault.azure.net/"),
    initializeService: !isDevelopment,
    addDataProtection: false,  // TODO (https://github.com/dotnet/arcade-services/issues/3815): Enable dataprotection
    addSwagger: useSwagger,
    apiRedirectionTarget: apiRedirectionTarget);

var app = builder.Build();

app.UseHttpsRedirection();
app.UseHsts();

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

    // In local dev with the `ng serve` scenario, just redirect /_/api to /api
    app.UseRewriter(new RewriteOptions().AddRewrite("^_/(.*)", "$1", true));

    // When running Maestro.Web locally (not through SF), we need to add compiled static files
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
        app.UseRewriter(new RewriteOptions().AddRedirect("^swagger(/ui)?/?$", "/swagger/ui/index.html"));
        app.UseSwagger();
        app.UseSwaggerUI(); // UseSwaggerUI Protected by if (env.IsDevelopment())
    }
}

app.MapWhen(
    ctx => ctx.Request.Path.StartsWithSegments("/api"),
    a => PcsStartup.ConfigureApi(a, isDevelopment, useSwagger, apiRedirectionTarget));

// Add security headers
app.Use(
    (ctx, next) =>
    {
        ctx.Response.OnStarting(() =>
        {
            if (!ctx.Response.Headers.ContainsKey("X-XSS-Protection"))
            {
                ctx.Response.Headers.Append("X-XSS-Protection", "1");
            }

            if (!ctx.Response.Headers.ContainsKey("X-Frame-Options"))
            {
                ctx.Response.Headers.Append("X-Frame-Options", "DENY");
            }

            if (!ctx.Response.Headers.ContainsKey("X-Content-Type-Options"))
            {
                ctx.Response.Headers.Append("X-Content-Type-Options", "nosniff");
            }

            if (!ctx.Response.Headers.ContainsKey("Referrer-Policy"))
            {
                ctx.Response.Headers.Append("Referrer-Policy", "no-referrer-when-downgrade");
            }

            return Task.CompletedTask;
        });

        return next();
    });

app.UseStatusCodePagesWithReExecute("/Error", "?code={0}");
app.UseCookiePolicy();
app.UseStaticFiles();

app.UseAuthentication();
app.UseRouting();
app.UseAuthorization();

app.MapDefaultEndpoints();
app.MapRazorPages();

if (isDevelopment)
{
    app.MapControllers().AllowAnonymous();
}
else
{
    app.MapControllers();
}

app.MapWhen(PcsStartup.IsGet, PcsStartup.AngularIndexHtmlRedirect);

app.Run();
