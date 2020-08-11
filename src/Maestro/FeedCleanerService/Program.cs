// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Maestro.AzureDevOps;
using Maestro.Data;
using Microsoft.DncEng.Configuration.Extensions;
using Microsoft.DotNet.Internal.DependencyInjection;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FeedCleanerService
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
                    host.RegisterStatelessService<FeedCleanerService>("FeedCleanerServiceType");
                    host.ConfigureServices(Configure);
                });
        }

        public static void Configure(IServiceCollection services)
        {
            services.Configure<FeedCleanerOptions>((options, provider) =>
            {
                var config1 = provider.GetRequiredService<IConfiguration>();
                options.Enabled = config1.GetSection("FeedCleaner").GetValue<bool>("Enabled");
                var releaseFeedsTokenMap = config1.GetSection("FeedCleaner:ReleasePackageFeeds").GetChildren();
                foreach (IConfigurationSection token1 in releaseFeedsTokenMap)
                {
                    options.ReleasePackageFeeds.Add((token1.GetValue<string>("Account"), token1.GetValue<string>("Project"), token1.GetValue<string>("Name")));
                }

                var azdoAccountTokenMap = config1.GetSection("AzureDevOps:Tokens").GetChildren();
                foreach (IConfigurationSection token2 in azdoAccountTokenMap)
                {
                    options.AzdoAccounts.Add(token2.GetValue<string>("Account"));
                }
            });
            services.AddDefaultJsonConfiguration();
            services.AddBuildAssetRegistry((provider, options) =>
            {
                var config = provider.GetRequiredService<IConfiguration>();
                options.UseSqlServer(config.GetSection("BuildAssetRegistry")["ConnectionString"]);
            });
            services.AddAzureDevOpsTokenProvider();
            services.Configure<AzureDevOpsTokenProviderOptions>((options, provider) =>
            {
                var config = provider.GetRequiredService<IConfiguration>();
                var tokenMap = config.GetSection("AzureDevOps:Tokens").GetChildren();
                foreach (IConfigurationSection token in tokenMap)
                {
                    options.Tokens.Add(token.GetValue<string>("Account"), token.GetValue<string>("Token"));
                }
            });
        }
    }
}
