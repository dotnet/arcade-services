// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.BarViz.Hosting;

var builder = WebApplication.CreateBuilder(args);

if (!builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException("The standalone BarViz.Hosting project can only be run in the Development environment.");
}

if (string.IsNullOrWhiteSpace(builder.Configuration["ApiRedirect:Uri"]))
{
    throw new InvalidOperationException("ApiRedirect:Uri must be configured in appsettings.Development.json to run the standalone BarViz.Hosting project.");
}

builder.Services.AddBarVizHosting();
builder.ConfigureApiRedirection();

var app = builder.Build();

app.UseWebAssemblyDebugging();
app.UseHttpsRedirection();
app.UseAntiforgery();
app.UseApiRedirection();
app.MapBarViz();

app.Run();
