// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.Api.Configuration;

public static class SpaDefaultPageMiddleware
{
    /// <summary>
    /// Redirects all requests to the default page (index.html).
    /// </summary>
    public static void UseSpa(this IApplicationBuilder app, Action<StaticFileOptions>? configureOptions = null)
    {
        var options = new SpaOptions();
        configureOptions?.Invoke(options);

        // Rewrite all requests to the default page
        app.Use((context, next) =>
        {
            // If we have an Endpoint, then this is a deferred match - just noop.
            if (context.GetEndpoint() != null)
            {
                return next(context);
            }

            context.Request.Path = options.DefaultPage;
            return next(context);
        });

        app.UseStaticFiles(options);

        app.Use((context, next) =>
        {
            return next(context);
        });
    }
}

public class SpaOptions : StaticFileOptions
{
    public string DefaultPage { get; set; } = "/index.html";
}
