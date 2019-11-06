// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.DotNet.Configuration.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DotNet.Status.Web
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            new WebHostBuilder()
                .UseKestrel()
                .UseContentRoot(Directory.GetCurrentDirectory())
                .ConfigureServices(services => { services.AddKeyVaultMappedConfiguration(); })
                .ConfigureAppConfiguration((host, config) =>
                {
                    config
                        .AddCommandLine(args)
                        .AddEnvironmentVariables();
                })
                .ConfigureLogging(
                    builder =>
                    {
                        builder.AddFilter(level => level > LogLevel.Debug);
                        builder.AddConsole();
                    })
                .UseStartup<Startup>()
                .UseUrls("http://localhost:5000/")
                .CaptureStartupErrors(true)
                .Build()
                .Run();
        }
    }
}
