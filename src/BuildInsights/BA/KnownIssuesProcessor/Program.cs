using System;
using Microsoft.DncEng.Configuration.Extensions;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Models;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Providers;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Services;
using Microsoft.Internal.Helix.GitHub.Providers;
using Microsoft.Internal.Helix.GitHub.Services;
using Microsoft.Internal.Helix.KnownIssues.Models;
using Microsoft.Internal.Helix.KnownIssues.Providers;
using Microsoft.Internal.Helix.KnownIssues.Services;
using Microsoft.Internal.Helix.KnownIssuesProcessor.Providers;
using Microsoft.Internal.Helix.KnownIssuesProcessor.Services;
using Microsoft.Internal.Helix.Utility;
using Microsoft.Internal.Helix.Utility.Azure;
using Microsoft.Internal.Helix.Utility.AzureDevOps.Models;
using Microsoft.Internal.Helix.Utility.AzureDevOps.Providers;
using Microsoft.Internal.Helix.Utility.Parallel;

namespace Microsoft.Internal.Helix.KnownIssuesProcessor
{
    public class Program
    {
        public static void Main(string[] args)
        {
            ServiceHost.Run(host =>
            {
                host.RegisterStatelessService<KnownIssuesProcessor>("KnownIssuesProcessorType");
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
            (string assemblyName, string assemblyVersion) = Helpers.GetAssemblyVersion();

            services.AddDefaultJsonConfiguration();
            services.Configure<ManagedIdentity>("Secrets", (o, c) => c.Bind(o));
            services.Configure<StorageQueueConnectionSettings>("KnownIssueStorageQueue", (o, c) => c.Bind(o));
            services.Configure<ParallelismSettings>("Parallelism", (o, c) => c.Bind(o));
            services.Configure<TableConnectionSettings>("KnownIssuesAnalysisTable", (o, c) => c.Bind(o));
            services.Configure<QueueProcessingThreadSettings>("QueueProcessing", (o, c) => c.Bind(o));
            services.Configure<KnownIssuesProcessorOptions>("KnownIssuesProcessorOptions", (o,c) => c.Bind(o));
            services.Configure<BuildAnalysisTableConnectionSettings>("BuildAnalysisTable", (o, c) => c.Bind(o));
            services.Configure<KnownIssueValidationTableConnectionSettings>("KnownIssuesValidationTable", (o, c) => c.Bind(o));
            services.Configure<KnownIssuesErrorsTableConnectionSettings>("KnownIssuesErrorsTable", (o, c) => c.Bind(o));
            services.Configure<AzureDevOpsSettingsCollection>("AzureDevOps", (o, c) => c.Bind(o));
            services.Configure<GitHubTokenProviderOptions>("GitHubAppAuth", (o, s) => s.Bind(o));
            services.Configure<GitHubClientOptions>("GitHubClient", (o, c) => c.Bind(o));
            services.Configure<GitHubClientOptions>(
                options => { options.ProductHeader = new Octokit.ProductHeaderValue(assemblyName, assemblyVersion); }
            );
            services.Configure<GitHubIssuesSettings>("GitHubIssuesSettings", (o, c) => c.Bind(o));
            services.AddVssConnection();
        }

        public static void AddServices(IServiceCollection services)
        {
            services.AddHttpClient();
            services.AddAzureStorageQueue();
            services.AddQueueProcessing<KnownIssuesMessageHandler>(
                queueOptions => { },
                parallelismOptions => { parallelismOptions.WorkerCount = Environment.ProcessorCount; }
            );
            services.Configure<TableConnectionSettings>("KnownIssuesAnalysisTable", (o, c) => c.Bind(o));
            services.Configure<ProcessingStatusTableConnectionSettings>("ProcessingStatusTable", (o, c) => c.Bind(o));

            services.TryAddSingleton<ITableClientFactory, TableClientFactory>();
            services.TryAddSingleton<IQueueClientFactory, QueueClientFactory>();
            services.TryAddSingleton<IKnownIssuesHistoryService, KnownIssuesHistoryProvider>();
            services.TryAddSingleton<IBuildAnalysisHistoryService, BuildAnalysisHistoryProvider>();
            services.TryAddSingleton<IRequestAnalysisService, RequestAnalysisProvider>();
            services.TryAddSingleton<IBuildProcessingStatusService, BuildProcessingStatusStatusProvider>();

            services.TryAddSingleton(_ => TimeProvider.System);
            services.TryAddSingleton<IGitHubClientFactory, GitHubClientFactory>();
            services.TryAddSingleton<IGitHubApplicationClientFactory, GitHubApplicationClientFactory>();
            services.TryAddSingleton<IInstallationLookup, InMemoryCacheInstallationLookup>();
            services.AddGitHubTokenProvider();
            services.TryAddSingleton<IGitHubChecksService, GitHubChecksProvider>();
            services.TryAddSingleton<IGitHubIssuesService, GitHubIssuesProvider>();

            services.AddVssConnection();
            services.AddAzureDevOpsBuildData();
        }
    }
}
