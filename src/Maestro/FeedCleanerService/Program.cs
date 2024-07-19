// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Common.AzureDevOpsTokens;
using Maestro.Data;
using Microsoft.DncEng.Configuration.Extensions;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace FeedCleanerService;

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
            var config = provider.GetRequiredService<IConfiguration>();
            options.Enabled = config.GetSection("FeedCleaner").GetValue<bool>("Enabled");
            var releaseFeedsTokenMap = config.GetSection("FeedCleaner:ReleasePackageFeeds").GetChildren();
            foreach (IConfigurationSection token1 in releaseFeedsTokenMap)
            {
                options.ReleasePackageFeeds.Add((token1.GetValue<string>("Account"), token1.GetValue<string>("Project"), token1.GetValue<string>("Name")));
            }

            AzureDevOpsTokenProviderOptions azdoConfig = [];
            config.GetSection("AzureDevOps").Bind(azdoConfig);
            options.AzdoAccounts.AddRange(azdoConfig.Keys);
        });
        services.AddDefaultJsonConfiguration();
        services.AddBuildAssetRegistry((provider, options) =>
        {
            var config = provider.GetRequiredService<IConfiguration>();
            options.UseSqlServerWithRetry(config.GetSection("BuildAssetRegistry")["ConnectionString"]);
        });
        services.AddSingleton<IAzureDevOpsTokenProvider, AzureDevOpsTokenProvider>();
        services.Configure<AzureDevOpsTokenProviderOptions>("AzureDevOps", (o, s) => s.Bind(o));
        services.TryAddTransient<IAzureDevOpsClient, AzureDevOpsClient>();
        services.TryAddTransient<ILogger>(sp => sp.GetRequiredService<ILogger<FeedCleanerService>>());
        services.TryAddTransient<IProcessManager>(sp => ActivatorUtilities.CreateInstance<ProcessManager>(sp, "git"));
    }
}
