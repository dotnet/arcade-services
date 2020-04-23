// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Reflection;
using Maestro.AzureDevOps;
using Maestro.Contracts;
using Maestro.Data;
using Maestro.MergePolicies;
using Microsoft.DncEng.Configuration.Extensions;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Octokit;

namespace SubscriptionActorService
{
    public static class Program
    {
        /// <summary>
        ///     This is the entry point of the service host process.
        /// </summary>
        private static void Main()
        {
            ServiceHost.Run(
                host =>
                {
                    host.RegisterStatefulActorService<SubscriptionActor>("SubscriptionActor");
                    host.RegisterStatefulActorService<PullRequestActor>("PullRequestActor");
                    host.ConfigureServices(Configure);
                });
        }

        public static void Configure(IServiceCollection services)
        {
            services.AddSingleton<IActionRunner, ActionRunner>();
            services.AddSingleton<IMergePolicyEvaluator, MergePolicyEvaluator>();
            services.AddSingleton<IRemoteFactory, DarcRemoteFactory>();
            services.AddSingleton<TemporaryFiles>();
            services.AddGitHubTokenProvider();
            services.AddAzureDevOpsTokenProvider();
            // We do not use AddMemoryCache here. We use our own cache because we wish to
            // use a sized cache and some components, such as EFCore, do not implement their caching
            // in such a way that will work with sizing.
            services.AddSingleton<DarcRemoteMemoryCache>();
            services.AddDefaultJsonConfiguration();
            services.AddBuildAssetRegistry((provider, options) =>
            {
                var config = provider.GetRequiredService<IConfiguration>();
                options.UseSqlServer(config.GetSection("BuildAssetRegistry")["ConnectionString"]);
            });
            services.Configure<GitHubClientOptions>(o =>
            {
                o.ProductHeader = new ProductHeaderValue("Maestro",
                    Assembly.GetEntryAssembly()
                        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                        ?.InformationalVersion);
            });
            services.Configure<GitHubTokenProviderOptions>("GitHub", (o, s) => s.Bind(o));
            services.Configure<AzureDevOpsTokenProviderOptions>((options, provider) =>
            {
                var config = provider.GetRequiredService<IConfiguration>();
                var tokenMap = config.GetSection("AzureDevOps:Tokens").GetChildren();
                foreach (IConfigurationSection token in tokenMap)
                {
                    options.Tokens.Add(token.GetValue<string>("Account"), token.GetValue<string>("Token"));
                }
            });

            services.AddMergePolicies();
        }
    }
}
