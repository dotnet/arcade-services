// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.FluentUI.AspNetCore.Components;
using ProductConstructionService.BarViz;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

const string PcsApiBaseAddress = "https://localhost:53180/";

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(PcsApiBaseAddress) });
builder.Services.AddFluentUIComponents();
builder.Services.AddSingleton(ProductConstructionService.Client.PcsApiFactory.GetAnonymous(PcsApiBaseAddress));

await builder.Build().RunAsync();
