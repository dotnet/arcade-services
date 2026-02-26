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

    var workQueueName = app.Configuration.GetRequiredValue(BuildInsightsConfiguration.ConfigurationKeys.WorkItemQueueName);
    var specialWorkQueueName = app.Configuration.GetRequiredValue(BuildInsightsConfiguration.ConfigurationKeys.SpecialWorkItemQueueName);
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
app.UseCookiePolicy();
app.UseAuthentication();
app.UseRouting();
app.UseAuthorization();
app.UseAntiforgery();
app.ConfigureSecurityHeaders();
app.UseOutputCache();

app.ConfigureApi("/api", isDevelopment);

app.MapDefaultEndpoints();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await app.SetWorkItemProcessorInitialState();

app.Run();
