// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using Blazored.SessionStorage;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components.Components.Tooltip;
using ProductConstructionService.BarViz;
using ProductConstructionService.BarViz.Code.Services;
using TextCopy;

// Needed for Newtonsoft.Json to work so it must not get trimmed away
// DynamicDependency attribute did not work for some reason
ImmutableList.CreateRange<int>([]);
ImmutableDictionary.CreateRange<int, int>([]);

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

string PcsApiBaseAddress = builder.HostEnvironment.IsDevelopment()
    ? "https://localhost:53180/"
    : builder.HostEnvironment.BaseAddress;

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(PcsApiBaseAddress) });
builder.Services.AddFluentUIComponents();
builder.Services.AddSingleton(PcsApiFactory.GetAnonymous(PcsApiBaseAddress));
builder.Services.InjectClipboard();
builder.Services.AddSingleton<UrlRedirectManager>();
builder.Services.AddBlazoredSessionStorage();
builder.Services.AddScoped<ITooltipService, TooltipService>();

await builder.Build().RunAsync();
