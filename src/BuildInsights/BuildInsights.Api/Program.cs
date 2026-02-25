// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.Api.Configuration;
using BuildInsights.ServiceDefaults;
using ProductConstructionService.Common;
using ProductConstructionService.WorkItems;

var builder = WebApplication.CreateBuilder(args);

bool isDevelopment = builder.Environment.IsDevelopment();
bool useSwagger = isDevelopment;

await builder.ConfigureBuildInsights(addKeyVault: true);
builder.AddServiceDefaults();
builder.AddRedisOutputCache("cache");

builder.Services
    .AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

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

//var controllers = app.MapControllers();
//if (isDevelopment)
//{
//    controllers.AllowAnonymous();
//}

await app.SetWorkItemProcessorInitialState();

app.Run();
