// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using Microsoft.DotNet.ServiceFabric.ServiceHost;

namespace Maestro.Web;

internal static class Program
{
    internal static int LocalHttpsPort => int.Parse(Environment.GetEnvironmentVariable("ASPNETCORE_HTTPS_PORT") ?? "443");

    private static void Main()
    {
        var options = new ServiceHostWebSiteOptions();

        // Run local Maestro.Web (when outside of SF) on HTTPS too
        if (!ServiceFabricHelpers.RunningInServiceFabric() && Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development")
        {
            options.Urls = options.Urls.Append($"https://localhost:{LocalHttpsPort}").ToList();
        }

        ServiceHostWebSite<Startup>.Run("Maestro.WebType", options);
    }
}
