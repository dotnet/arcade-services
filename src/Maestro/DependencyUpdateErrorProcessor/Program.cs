// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Reflection;
using System.Threading.Tasks;
using Autofac;
using Maestro.Data;
using Microsoft.DncEng.Configuration.Extensions;
using Microsoft.Dotnet.GitHub.Authentication;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Packaging;
using Octokit;

namespace DependencyUpdateErrorProcessor
{
    internal static class Program
    {
        /// <summary>
        /// This is the entry point of the service host process.
        /// </summary>
        private static void Main()
        {
            ServiceHost.Run(
                host =>
                {
                    host.RegisterStatefulService<DependencyUpdateErrorProcessor>("DependencyUpdateErrorProcessorType");
                    host.ConfigureServices(
                        services =>
                        {
                            services.AddDefaultJsonConfiguration();
                            services.AddBuildAssetRegistry(
                                (provider, options) =>
                                {
                                    var config = provider.GetRequiredService<IConfiguration>();
                                    options.UseSqlServer(config.GetSection("BuildAssetRegistry")["ConnectionString"]);
                                });
                            services.AddGitHubTokenProvider();
                            services.Configure<GitHubClientOptions>(o =>
                            {
                                o.ProductHeader = new ProductHeaderValue("Maestro", Assembly.GetEntryAssembly()
                                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                                    ?.InformationalVersion);
                            });
                            services.Configure<GitHubTokenProviderOptions>(
                                (options, provider) =>
                                {
                                    var config = provider.GetRequiredService<IConfiguration>();
                                    IConfigurationSection section = config.GetSection("GitHub");
                                    section.Bind(options);
                                });
                            services.Configure<DependencyUpdateErrorProcessorOptions>(
                                (options, provider) =>
                                {
                                    var config = provider.GetRequiredService<IConfiguration>();
                                    options.IsEnabled = bool.Parse(config["EnableDependencyUpdateErrorProcessor"]);
                                    options.GithubUrl = config["GithubUrl"];
                                    options.FyiHandle = config["FyiHandle"];
                                });
                        });
                    host.ConfigureContainer(builder => builder.Register((c, p) =>
                    {
                        var repo = p.TypedAs<string>();
                        var context = c.Resolve<BuildAssetRegistryContext>();
                        var gitHubTokenProvider = c.Resolve<IGitHubTokenProvider>();
                        long installationId = Task
                            .Run(async () => 
                                await context.GetInstallationId(repo)).GetAwaiter().GetResult();
                        string gitHubToken = Task
                            .Run(async () => 
                                await gitHubTokenProvider.GetTokenForInstallationAsync(installationId)).GetAwaiter().GetResult();
                        string version = Assembly
                            .GetExecutingAssembly()
                            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                            .InformationalVersion;
                        ProductHeaderValue product = new ProductHeaderValue("Maestro", version);
                        return new GitHubClient(product)
                        {
                            Credentials = new Credentials(gitHubToken),
                        };
                    }).As<GitHubClient>().InstancePerDependency());
                });
        }
    }
}
