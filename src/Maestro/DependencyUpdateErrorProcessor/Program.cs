// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Reflection;
using Azure.Core;
using Azure.Identity;
using Maestro.AzureDevOps;
using Maestro.Data;
using Microsoft.DncEng.Configuration.Extensions;
using Microsoft.Dotnet.GitHub.Authentication;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
                            services.AddAzureDevOpsTokenProvider();
                            services.Configure<AzureDevOpsTokenProviderOptions>(
                                (options, provider) =>
                                {
                                    var config = provider.GetRequiredService<IConfiguration>();
                                    var tokenMap = config.GetSection("AzureDevOps:Tokens").GetChildren();
                                    foreach (IConfigurationSection token in tokenMap)
                                    {
                                        options.Tokens.Add(token.GetValue<string>("Account"), token.GetValue<string>("Token"));
                                    }
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
                                
                                string apiEndPointUri = config["AppConfigurationUri"];
                                ConfigurationBuilder builder = new ConfigurationBuilder();
                                TokenCredential credential = apiEndPointUri.Contains("maestrolocal") ?
                                    new DefaultAzureCredential() :
                                    (TokenCredential)new ManagedIdentityCredential();
                                builder.AddAzureAppConfiguration(o =>
                                {
                                    o.Connect(new Uri(apiEndPointUri), credential)
                                    .ConfigureRefresh(refresh =>
                                        {
                                            refresh.Register(".appconfig.featureflag/DependencyUpdateErrorProcessor")
                                                .SetCacheExpiration(TimeSpan.FromSeconds(1));
                                        }).UseFeatureFlags();

                                    options.ConfigurationRefresherEndPointUri = o.GetRefresher();
                                });
                                options.DynamicConfigs = builder.Build();
                                options.GithubUrl = config["GithubUrl"];
                                options.Owner = config["Owner"];
                                options.Repository = config["Repository"];
                                options.FyiHandle = config["FyiHandle"];
                            });
                        });
                });
        }
    }
}
