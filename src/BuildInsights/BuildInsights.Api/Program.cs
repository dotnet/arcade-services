// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.Api.Components;
using BuildInsights.ServiceDefaults;
using BuildInsights.ServiceDefaults.Configuration;
using Maestro.Common;
using ProductConstructionService.WorkItems;

var builder = WebApplication.CreateBuilder(args);

bool isDevelopment = builder.Environment.IsDevelopment();

await builder.ConfigureBuildInsights(addKeyVault: true);

var app = builder.Build();

if (isDevelopment)
{
    app.UseDeveloperExceptionPage();
    app.MapOpenApi();

    var workQueueName = app.Configuration.GetRequiredValue(BuildInsightsCommonConfiguration.ConfigurationKeys.WorkItemQueueName);
    var specialWorkQueueName = app.Configuration.GetRequiredValue(BuildInsightsCommonConfiguration.ConfigurationKeys.SpecialWorkItemQueueName);
    await app.Services.UseLocalWorkItemQueues([workQueueName, specialWorkQueueName]);
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

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

app.UseHttpsRedirection();
app.UseHttpLogging();
app.UseCookiePolicy();
app.UseAuthentication();
app.UseRouting();
app.UseAuthorization();
app.UseAntiforgery();
app.ConfigureSecurityHeaders();
app.ConfigureApi("/api", isDevelopment, app.Configuration);
app.MapDefaultEndpoints();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

if (isDevelopment)
{
    // auto execute migrations in development phase
    await app.InitializeDatabaseMigrations();
}

await app.SetWorkItemProcessorInitialState();

app.Run();
