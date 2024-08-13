// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Identity;
using Azure.Storage.Queues;
using Microsoft.AspNetCore.ApiVersioning;
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
    ManagedIdentityClientId = builder.Configuration[ConfigurationKeys.ManagedIdentityId]
});

bool isDevelopment = builder.Environment.IsDevelopment();
var apiRedirection = builder.Configuration.GetSection(ConfigurationKeys.ApiRedirectionConfiguration);
bool apiRedirectionEnabled = apiRedirection.Exists();
string? apiRedirectionTarget = apiRedirectionEnabled
    ? apiRedirection[ConfigurationKeys.ApiRedirectionTarget] ?? throw new Exception($"{ConfigurationKeys.ApiRedirectionConfiguration}:{ConfigurationKeys.ApiRedirectionTarget} is missing")
    : null;

bool useSwagger = isDevelopment;

builder.ConfigurePcs(
    vmrPath: vmrPath,
    tmpPath: tmpPath,
    vmrUri: vmrUri,
    azureCredential: credential,
    keyVaultUri: new Uri($"https://{builder.Configuration.GetRequiredValue(ConfigurationKeys.KeyVaultName)}.vault.azure.net/"),
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
        app.Use(
            (ctx, next) =>
            {
                if (ctx.Request.Path == "/swagger.json")
                {
                    var vcp = ctx.RequestServices.GetRequiredService<VersionedControllerProvider>();
                    string highestVersion = vcp.Versions.Keys.OrderByDescending(n => n).First();
                    ctx.Request.Path = $"/swagger/{highestVersion}/swagger.json";
                }

                return next();
            });

        app.UseSwagger();
        app.UseSwaggerUI(options => // Enable Swagger UI only in local dev env
        {
            options.DocumentTitle = "Product Construction Service API";

            var versions = app.Services.GetRequiredService<VersionedControllerProvider>().Versions.Keys
                .OrderDescending();

            foreach (var version in versions)
            {
                options.SwaggerEndpoint($"/swagger/{version}/swagger.json", $"Product Construction Service API {version}");
            }
        });
    }
}

app.UseStatusCodePagesWithReExecute("/Error", "?code={0}");
app.UseCookiePolicy();
app.UseStaticFiles();

app.UseAuthentication();
app.UseRouting();
app.UseAuthorization();

// Map API controllers
app.MapWhen(
    ctx => ctx.Request.Path.StartsWithSegments("/api"),
    a => PcsStartup.ConfigureApi(a, isDevelopment, apiRedirectionTarget));

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
