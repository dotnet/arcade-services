using Microsoft.AspNetCore.Hosting;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.GitHub.Authentication;
using System.Reflection;
using RolloutScorer.Services;
using RolloutScorer.Providers;
using Microsoft.DotNet.Internal.AzureDevOps;
using Octokit;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.DncEng.Configuration.Extensions;

namespace RolloutScorer.Service
{
    public class Program
    {
        public static void Main(string[] args)
        {
            ServiceHost.Run(host =>
            {
                host.RegisterStatelessService<RolloutScorerProcessor>("RolloutScorerProcessorType");
                host.ConfigureServices(ConfigureServices);
            });
        }

        public static void ConfigureServices(IServiceCollection services)
        {
            AddServices(services);
            SetConfiguration(services);
        }

        private static void SetConfiguration(IServiceCollection services)
        {
            (string assemblyName, string assemblyVersion) = GetAssemblyVersion();

            services.AddDefaultJsonConfiguration();
            services.Configure<GitHubClientOptions>(
                options => { options.ProductHeader = new Octokit.ProductHeaderValue(assemblyName, assemblyVersion); }
            );
            services.Configure<GitHubTokenProviderOptions>("GitHubAppAuth", (o, s) => s.Bind(o));
            services.Configure<AzureDevOpsSettings>("AzureDevOps", (o, c) => c.Bind(o));
            services.Configure<GitHubSettings>("GitHub", (o, c) => c.Bind(o));
        }

        public static void AddServices(IServiceCollection services)
        {
            services.AddSingleton<IScorecardService, ScorecardProvider>();
            services.AddSingleton<IRolloutScorerService, RolloutScorerProvider>();
            services.AddSingleton<IIssueService, IssueProvider>();

            services.AddSingleton<IGitHubClientFactory, GitHubClientFactory>();
            services.AddSingleton<IGitHubApplicationClientFactory, GitHubApplicationClientFactory>();
            services.AddGitHubTokenProvider();
            services.AddSingleton<IAzureDevOpsClientFactory, AzureDevOpsClientFactory>();

            (string assemblyName, string assemblyVersion) = GetAssemblyVersion();

            services.AddSingleton<IGitHubClient>(
                provider =>
                {
                    var options = provider.GetRequiredService<IOptions<GitHubSettings>>().Value;
                    var clientFactory = provider.GetRequiredService<IGitHubApplicationClientFactory>();
                    return clientFactory.CreateGitHubClientAsync(options.Organization, options.Repository).Result;
                });

            services.AddSingleton<VssCredentials>(
                provider =>
                {
                    var options = provider.GetRequiredService<IOptions<AzureDevOpsSettings>>();
                    return new VssBasicCredential("", options.Value.AccessToken);
                }
            );

            services.AddSingleton<VssConnection>(
                p =>
                {
                    var options = p.GetRequiredService<IOptions<AzureDevOpsSettings>>().Value;
                    var credentials = p.GetRequiredService<VssCredentials>();
                    var settings = new VssClientHttpRequestSettings
                    {
                        UserAgent = new List<ProductInfoHeaderValue>
                            {new ProductInfoHeaderValue(assemblyName, assemblyVersion)}
                    };
                    return new VssConnection(
                        new Uri(options.CollectionUri, UriKind.Absolute),
                        new VssHttpMessageHandler(credentials, settings),
                        p.GetServices<AzureDevOpsDelegatingHandler>()
                    );
                }
            );

            services.TryAddSingleton(p => p.GetRequiredService<VssConnection>().GetClient<BuildHttpClient>());


            //services.AddAzureStorageQueue();
            //services.AddQueueProcessing<AnalysisProcessor>(
            //    queueOptions => { },
            //    parallelismOptions => { parallelismOptions.WorkerCount = Environment.ProcessorCount; }
            //);

            //services.AddUserSentiment(o => o.Host = "https://helix.int-dot.net");
            //services.AddMarkdownGenerator();

            //services.TryAddSingleton<IGitHubChecksService, GitHubCheckService>();
            //services.TryAddSingleton<IBuildDataService, BuildDataProvider>();
            //services.TryAddSingleton<IRelatedBuildService, RelatedBuildProvider>();

            //services.TryAddScoped<IBuildAnalysisService, BuildAnalysisProvider>();
            //services.TryAddScoped<IMergedBuildAnalysisService, MergedBuildAnalysisProvider>();
            //services.TryAddScoped<IPreviousBuildAnalysisService, PreviousBuildAnalysisProvider>();
            //services.TryAddScoped<IBuildCacheService, BuildCacheProvider>();

            //services.AddSingleton<AzureDevOpsDelegatingHandler, RetryAfterHandler>();
            //services.AddSingleton<AzureDevOpsDelegatingHandler, ThrottlingHeaderLoggingHandler>();
            //services.AddSingleton<AzureDevOpsDelegatingHandler, LoggingHandler>();



            //services.TryAddSingleton(p => p.GetRequiredService<VssConnection>().GetClient<TestManagementHttpClient>());
            //services.TryAddSingleton(p => p.GetRequiredService<VssConnection>().GetClient<BuildHttpClient>());
            //services.AddSingleton<IGitHubClientFactory, GitHubClientFactory>();
            //services.AddSingleton<IGitHubApplicationClientFactory, GitHubApplicationClientFactory>();
            //services.AddSingleton<IInstallationLookup, InMemoryCacheInstallationLookup>();

            //services.TryAddScoped<IContextualStorage, BlobContextualStorage>();
            //services.TryAddScoped<IDistributedLockService, BlobContextualStorage>();
            //services.TryAddScoped<IBlobClientFactory, BlobClientFactory>();
        }

        private static (string assemblyName, string assemblyVersion) GetAssemblyVersion()
        {
            string assemblyName = Assembly.GetEntryAssembly()?.GetName().Name ?? "RolloutScorer";
            string assemblyVersion =
                Assembly.GetEntryAssembly()
                    ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion ??
                "42.42.42.42";
            return (assemblyName, assemblyVersion);
        }
    }
}
