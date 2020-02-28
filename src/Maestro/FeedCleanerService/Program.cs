// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Maestro.AzureDevOps;
using Maestro.Data;
using Microsoft.DncEng.Configuration.Extensions;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FeedCleanerService
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
                    host.RegisterStatelessService<FeedCleanerService>("FeedCleanerServiceType");
                    host.ConfigureServices(
                        services =>
                        {
                            services.Configure<FeedCleanerOptions>(
                                (options, provider) =>
                                {
                                    var config = provider.GetRequiredService<IConfigurationRoot>();
                                    options.Enabled = config.GetSection("FeedCleaner").GetValue<bool>("Enabled");
                                    var releaseFeedsTokenMap = config.GetSection("FeedCleaner:ReleasePackageFeeds").GetChildren();
                                    foreach (IConfigurationSection token in releaseFeedsTokenMap)
                                    {
                                        options.ReleasePackageFeeds.Add((
                                            token.GetValue<string>("Account"),
                                            token.GetValue<string>("Project"),
                                            token.GetValue<string>("Name")));
                                    }

                                    var azdoAccountTokenMap = config.GetSection("AzureDevOps:Tokens").GetChildren();
                                    foreach (IConfigurationSection token in azdoAccountTokenMap)
                                    {
                                        options.AzdoAccounts.Add(token.GetValue<string>("Account"));
                                    }
                                }
                                );
                            services.AddDefaultJsonConfiguration();
                            services.AddBuildAssetRegistry(
                                (provider, options) =>
                                {
                                    var config = provider.GetRequiredService<IConfigurationRoot>();
                                    options.UseSqlServer(config.GetSection("BuildAssetRegistry")["ConnectionString"]);
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
                        });
                });
        }
    }
}
