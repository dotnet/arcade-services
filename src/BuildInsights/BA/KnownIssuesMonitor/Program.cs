using System;
using System.Net.Http.Headers;
using Microsoft.DncEng.Configuration.Extensions;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.Kusto;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Internal.Helix.KnownIssues.Models;
using Microsoft.Internal.Helix.KnownIssues.Providers;
using Microsoft.Internal.Helix.KnownIssues.Services;
using Microsoft.Internal.Helix.Utility;
using Microsoft.Internal.Helix.Utility.Azure;
using Microsoft.Internal.Helix.Utility.GitHubGraphQL;

namespace Microsoft.Internal.Helix.KnownIssuesMonitor
{
    public class Program
    {
        public static void Main(string[] args)
        {
            ServiceHost.Run(host =>
            {
                host.RegisterStatelessService<KnownIssuesMonitor>("KnownIssuesMonitorType");
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
            services.Configure<GitHubTokenProviderOptions>("GitHubAppAuth", (o, c) => c.Bind(o));
            services.Configure<GitHubClientOptions>("GitHubClient", (o, c) => c.Bind(o));
            services.Configure<GitHubClientOptions>(
                options => { options.ProductHeader = new Octokit.ProductHeaderValue(assemblyName, assemblyVersion); }
            );
            services.Configure<GitHubGraphQLOptions>("GitHubGraphQLOptions", (o, c) => c.Bind(o));
            services.Configure<GitHubIssuesSettings>("GitHubIssuesSettings", (o, c) => c.Bind(o));
            services.Configure<TableConnectionSettings>("KnownIssuesAnalysisTable", (o, c) => c.Bind(o));
            services.Configure<KnownIssuesErrorsTableConnectionSettings>("KnownIssuesErrorsTable", (o, c) => c.Bind(o));
            services.Configure<KnownIssueValidationTableConnectionSettings>("KnownIssuesValidationTable", (o, c) => c.Bind(o));
            services.Configure<KnownIssuesProjectOptions>("KnownIssuesProjectOptions", (o, c) => c.Bind(o));
            services.Configure<KustoOptions>("Kusto", (o, c) => c.Bind(o));
            services.Configure<SsaCriteriaSettings>("SsaCriteriaSettings", (o, c) => c.Bind(o));

            // GitHub will reject authenticated requests with a 403 Forbidden if the UserAgent isn't set
            services.Configure<HttpClientFactoryOptions>(options =>
            {
                options.HttpClientActions.Add(client =>
                {
                    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(assemblyName, assemblyVersion));
                });
            });
        }

        public static void AddServices(IServiceCollection services)
        {
            services.AddHttpClient();

            services.AddSingleton<IGitHubClientFactory, GitHubClientFactory>();
            services.AddSingleton<IGitHubApplicationClientFactory, GitHubApplicationClientFactory>();
            services.AddSingleton<IInstallationLookup, InMemoryCacheInstallationLookup>();
            services.AddGitHubTokenProvider();
            services.AddSingleton(_ => TimeProvider.System);

            services.AddSingleton<IGitHubGraphQLClient, GitHubGraphQLClient>();
            services.AddSingleton<IGitHubGraphQLHttpClientFactory, GitHubGraphQLAppHttpClientFactory>();

            services.AddKustoClientProvider("Kusto");
            services.AddSingleton(p => p.GetRequiredService<IKustoIngestClientFactory>().GetClient());
            services.AddSingleton<IKustoIngestClientFactory, KustoIngestClientFactory>();

            services.AddSingleton<IGitHubIssuesService, GitHubIssuesProvider>();
            services.AddSingleton<ITableClientFactory, TableClientFactory>();
            services.AddSingleton<IKnownIssuesService, KnownIssuesProvider>();
            services.AddSingleton<IKnownIssuesHistoryService, KnownIssuesHistoryProvider>();
            services.AddSingleton<IKnownIssueReporter, KnownIssueReporter>();
            services.AddSingleton<KnownIssuesReportHelper>();
        }
    }
}
