// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.Api.Components;
using BuildInsights.Api.Configuration;
using BuildInsights.ServiceDefaults;
using ProductConstructionService.Common;
using ProductConstructionService.WorkItems;

var builder = WebApplication.CreateBuilder(args);

bool isDevelopment = builder.Environment.IsDevelopment();
bool useSwagger = isDevelopment;

// Add service defaults & Aspire client integrations.
await builder.ConfigureBuildInsights(
    authRedis: !isDevelopment,
    addSwagger: useSwagger);
builder.AddServiceDefaults();
builder.AddRedisOutputCache("cache");

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (isDevelopment)
{
    app.UseDeveloperExceptionPage();

    var workQueueName = app.Configuration.GetRequiredValue("WorkItemQueueName");
    await app.Services.UseLocalWorkItemQueues([workQueueName]);

    if (useSwagger)
    {
        app.UseLocalSwagger();
    }
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseHttpLogging();

app.UseAntiforgery();

app.UseOutputCache();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapDefaultEndpoints();

var controllers = app.MapControllers();
if (isDevelopment)
{
    controllers.AllowAnonymous();
}

await app.SetWorkItemProcessorInitialState();

app.Run();
