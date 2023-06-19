// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using Maestro.AzureDevOps;
using Maestro.Data;
using Maestro.DataProviders;
using Microsoft.DncEng.Configuration.Extensions;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.Kusto;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DependencyUpdater;

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
                host.RegisterStatefulService<DependencyUpdater>("DependencyUpdaterType");
                host.ConfigureServices(Configure);
            });
    }

    public static void Configure(IServiceCollection services)
    {
        services.AddDefaultJsonConfiguration();
        services.AddBuildAssetRegistry((provider, options) =>
        {
            var config = provider.GetRequiredService<IConfiguration>();
            options.UseSqlServerWithRetry(config.GetSection("BuildAssetRegistry")["ConnectionString"]);
        });

        services.Configure<GitHubClientOptions>(o =>
        {
            o.ProductHeader = new Octokit.ProductHeaderValue("Maestro",
                Assembly.GetEntryAssembly()
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion);
        });
        services.Configure<GitHubTokenProviderOptions>("GitHub", (o, s) =>
        {
            s.Bind(o);
        });
        services.AddGitHubTokenProvider();

        services.AddAzureDevOpsTokenProvider();
        services.Configure<AzureDevOpsTokenProviderOptions>("AzureDevOps:Tokens", (o, s) =>
        {
            var tokenMap = s.GetChildren();
            foreach (IConfigurationSection token in tokenMap)
            {
                o.Tokens.Add(token.GetValue<string>("Account"), token.GetValue<string>("Token"));
            }
        });

        // We do not use AddMemoryCache here. We use our own cache because we wish to
        // use a sized cache and some components, such as EFCore, do not implement their caching
        // in such a way that will work with sizing.
        services.AddSingleton<DarcRemoteMemoryCache>();

        services.AddTransient<IVersionDetailsParser, VersionDetailsParser>();
        services.AddScoped<IRemoteFactory, DarcRemoteFactory>();
        services.AddKustoClientProvider("Kusto");
    }
}
