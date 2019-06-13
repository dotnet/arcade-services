// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Maestro.AzureDevOps;
using Maestro.Contracts;
using Maestro.Data;
using Maestro.GitHub;
using Microsoft.AspNetCore.Hosting;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace ReleasePipelineRunner
{
    internal static class Program
    {
        /// <summary>
        ///     This is the entry point of the service host process.
        /// </summary>
        private static void Main()
        {
            ServiceHost.Run(
                host =>
                {
                    host.RegisterStatefulService<ReleasePipelineRunner>("ReleasePipelineRunnerType");
                    host.ConfigureContainer(builder => { builder.AddServiceFabricService<IDependencyUpdater>("fabric:/MaestroApplication/DependencyUpdater"); });
                    host.ConfigureServices(
                        services =>
                        {
                            services.AddSingleton(
                                provider => ServiceHostConfiguration.Get(
                                    provider.GetRequiredService<IHostingEnvironment>()));
                            services.AddDbContext<BuildAssetRegistryContext>(
                                (provider, options) =>
                                {
                                    var config = provider.GetRequiredService<IConfigurationRoot>();
                                    options.UseSqlServer("Server=tcp:maestro-int-server.database.windows.net,1433;Initial Catalog=BuildAssetRegistry;Persist Security Info=False;User ID=Maestro_Access;Password=UxQewU{gx@dgm0gjrfqhoz18msFT7_&#$!~<vl_czd=is:gx;MultipleActiveResultSets=True;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;");
                                });
                            services.AddAzureDevOpsTokenProvider();
                            services.Configure<AzureDevOpsTokenProviderOptions>(
                                (options, provider) =>
                                {
                                    var config = provider.GetRequiredService<IConfigurationRoot>();
                                    var tokenMap = config.GetSection("AzureDevOps:Tokens").GetChildren();
                                    foreach (IConfigurationSection token in tokenMap)
                                    {
                                        options.Tokens.Add(token.GetValue<string>("Account"), token.GetValue<string>("Token"));
                                    }
                                });
                            services.AddGitHubTokenProvider();
                            services.Configure<GitHubTokenProviderOptions>(
                                (options, provider) =>
                                {
                                    var config = provider.GetRequiredService<IConfigurationRoot>();
                                    IConfigurationSection section = config.GetSection("GitHub");
                                    section.Bind(options);
                                    options.ApplicationName = "Maestro";
                                    options.ApplicationVersion = Assembly.GetEntryAssembly()
                                        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                                        ?.InformationalVersion;
                                });
                        });
                });
        }
    }
}
