// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.DncEng.Configuration.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DotNet.Status.Web
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            Host.CreateDefaultBuilder()
                .ConfigureWebHostDefaults(host =>
                {
                    host.UseStartup<Startup>()
                        .UseUrls("http://localhost:5000/")
                        .CaptureStartupErrors(true);
                })
                .UseContentRoot(Directory.GetCurrentDirectory())
                .ConfigureAppConfiguration((host, config) =>
                {
                    config
                        .AddCommandLine(args)
                        .AddUserSecrets(Assembly.GetEntryAssembly())
                        .AddEnvironmentVariables()
                        .AddDefaultJsonConfiguration(host.HostingEnvironment, "appsettings{0}.json");
                })
                .ConfigureLogging(
                    builder =>
                    {
                        builder.AddFilter(level => level > LogLevel.Debug);
                        builder.AddConsole();
                    })
                .Build()
                .Run();
        }
    }
}
