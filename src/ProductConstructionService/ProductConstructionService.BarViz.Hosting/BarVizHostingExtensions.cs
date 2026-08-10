// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.BarViz.Hosting;

public static class BarVizHostingExtensions
{
    public static IServiceCollection AddBarVizHosting(this IServiceCollection services)
    {
        services
            .AddRazorComponents()
            .AddInteractiveWebAssemblyComponents();

        return services;
    }

    public static void MapBarViz(this WebApplication app, string? authorizationPolicy = null)
    {
        app.MapStaticAssets();

        var endpoints = app.MapRazorComponents<BarViz.App>()
            .AddInteractiveWebAssemblyRenderMode()
            .AddAdditionalAssemblies(typeof(ClientApp).Assembly);

        if (!string.IsNullOrEmpty(authorizationPolicy))
        {
            endpoints.RequireAuthorization(authorizationPolicy);
        }
    }
}
