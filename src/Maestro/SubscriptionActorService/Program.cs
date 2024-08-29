// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using Maestro.Common.AzureDevOpsTokens;
using Maestro.Data;
using Maestro.DataProviders;
using Maestro.MergePolicies;
using Microsoft.DncEng.Configuration.Extensions;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.Kusto;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Octokit;
using ProductConstructionService.Client;

namespace SubscriptionActorService;

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
        services.TryAddTransient<ILogger>(sp => sp.GetRequiredService<ILogger<SubscriptionActor>>());
        services.AddTransient<IProcessManager>(sp =>
            ActivatorUtilities.CreateInstance<ProcessManager>(sp, sp.GetRequiredService<ILocalGit>().GetPathToLocalGit()));
        services.AddSingleton<IActionRunner, ActionRunner>();
        services.AddSingleton<IMergePolicyEvaluator, MergePolicyEvaluator>();
        services.AddTransient<ICoherencyUpdateResolver, CoherencyUpdateResolver>();
        services.AddSingleton<ILocalGit, LocalGit>();
        services.AddTransient<IVersionDetailsParser, VersionDetailsParser>();
        services.AddScoped<IRemoteFactory, DarcRemoteFactory>();
        services.AddScoped<IBasicBarClient, SqlBarClient>();
        services.AddTransient<IPullRequestBuilder, PullRequestBuilder>();
        services.AddSingleton<TemporaryFiles>();
        services.AddGitHubTokenProvider();
        services.AddSingleton<IAzureDevOpsTokenProvider, AzureDevOpsTokenProvider>();
        services.AddTransient<IPullRequestPolicyFailureNotifier, PullRequestPolicyFailureNotifier>();
        // We do not use AddMemoryCache here. We use our own cache because we wish to
        // use a sized cache and some components, such as EFCore, do not implement their caching
        // in such a way that will work with sizing.
        services.AddSingleton<DarcRemoteMemoryCache>();
        services.AddDefaultJsonConfiguration();
        services.AddBuildAssetRegistry((provider, options) =>
        {
            var config = provider.GetRequiredService<IConfiguration>();
            options.UseSqlServerWithRetry(config.GetSection("BuildAssetRegistry")["ConnectionString"]);
        });
        services.AddSingleton<IGitHubClientFactory, GitHubClientFactory>();
        services.Configure<GitHubClientOptions>(o =>
        {
            o.ProductHeader = new ProductHeaderValue("Maestro",
                Assembly.GetEntryAssembly()
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion);
        });
        services.Configure<GitHubTokenProviderOptions>("GitHub", (o, s) => s.Bind(o));
        services.Configure<AzureDevOpsTokenProviderOptions>("AzureDevOps", (o, s) => s.Bind(o));
        services.AddSingleton<IProductConstructionServiceApi>(s =>
        {
            var config = s.GetRequiredService<IConfiguration>();
            var uri = config["ProductConstructionService:Uri"];

            var noAuth = config.GetValue<bool>("ProductConstructionService:NoAuth");
            if (noAuth)
            {
                return PcsApiFactory.GetAnonymous(uri);
            }

            return PcsApiFactory.GetAuthenticated(uri, managedIdentityId: "system", disableInteractiveAuth: true);
        });

        services.AddMergePolicies();
        services.AddKustoClientProvider("Kusto");
    }
}
