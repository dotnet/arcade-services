// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.Api.Components;
using BuildInsights.Api.Configuration;
using BuildInsights.ServiceDefaults;
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

    var workQueueName = app.Configuration.GetRequiredValue(BuildInsightsStartup.ConfigurationKeys.WorkItemQueueName);
    var specialWorkQueueName = app.Configuration.GetRequiredValue(BuildInsightsStartup.ConfigurationKeys.SpecialWorkItemQueueName);
    await app.Services.UseLocalWorkItemQueues([workQueueName, specialWorkQueueName]);
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
app.MapDefaultEndpoints();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

var controllers = app.MapControllers();
if (isDevelopment)
{
    controllers.AllowAnonymous();
}

await app.SetWorkItemProcessorInitialState();

app.Run();
