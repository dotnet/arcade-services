// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.Api.Configuration;

internal static class SpaApplicationBuilderExtensions
{
    /// <summary>
    /// A workaround - our custom simplified version of UseSpa that does not throw for OPTION requests.
    /// This was caused by out combination of WASM Blazor + ASP.NET Razor.
    /// Redirect all requests to /index.html (if not an actual file).
    /// See https://github.com/dotnet/arcade-services/issues/4339
    /// See https://github.com/dotnet/aspnetcore/blob/12d57ddde6bd2f47757364252596cfa89df2ef60/src/Middleware/Spa/SpaServices.Extensions/src/StaticFiles/SpaStaticFilesExtensions.cs
    /// </summary>
    public static void UseSpa(this IApplicationBuilder app)
    {
        // Rewrite all requests to the default page
        app.Use((context, next) =>
        {
            // If we have an Endpoint, then this is a deferred match - just noop.
            if (context.GetEndpoint() != null)
            {
                return next(context);
            }

            context.Request.Path = "/index.html";
            return next(context);
        });

        // Serve it as a static file
        app.UseStaticFiles();

        app.Use((context, next) =>
        {
            // If we have an Endpoint, then this is a deferred match - just noop.
            if (context.GetEndpoint() != null)
            {
                return next(context);
            }

            // The default file didn't get served as a static file
            context.Response.StatusCode = 404;
            return Task.CompletedTask;
        });
    }
}
