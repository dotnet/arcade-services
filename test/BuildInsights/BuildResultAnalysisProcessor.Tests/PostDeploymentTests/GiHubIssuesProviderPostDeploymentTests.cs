using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.Internal.Testing.DependencyInjection.Abstractions;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Helix.Test.Utilities;
using Microsoft.Internal.Helix.KnownIssues.Providers;
using Microsoft.Internal.Helix.KnownIssues.Services;
using NUnit.Framework;
using Octokit;

namespace Microsoft.Internal.Helix.BuildResultAnalysisProcessor.Tests.PostDeploymentTests;

[Category("SkipWhenLiveUnitTesting")]
[Category("PostDeployment")]
[Category("Staging")]
[TestFixture]
public partial class GiHubIssuesProviderPostDeploymentTests
{
    [TestDependencyInjectionSetup]
    public static class TestConfig
    {
        public static void DefaultSetup(IServiceCollection services)
        {
            services.AddDefaultTestsJsonConfiguration();
            services.AddGitHubTokenProvider();
            services.AddSingleton(_ => TimeProvider.System);
            services.AddLogging(l => l.AddProvider(new NUnitLogger()));
            services.AddSingleton<ISystemClock, SystemClock>();
            services.AddSingleton<ExponentialRetry>();
            services.AddSingleton<IHostEnvironment>(new HostingEnvironment
            {
                EnvironmentName = Environments.Staging,
                ContentRootPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            });

            (string assemblyName, string assemblyVersion) = GetAssemblyVersion();
            services.Configure<GitHubTokenProviderOptions>("GitHubAppAuth", (o, s) => s.Bind(o));
            services.Configure<GitHubAccess>("GitHubAccess", (o, s) => s.Bind(o));
            services.Configure<GitHubClientOptions>(options =>
            {
                options.ProductHeader = new ProductHeaderValue(assemblyName, assemblyVersion);
            });

            services.AddSingleton<IGitHubClientFactory, GitHubClientFactory>();
            services.AddSingleton<IGitHubApplicationClientFactory, GitHubApplicationClientFactory>();
            services.AddSingleton<IInstallationLookup, InMemoryCacheInstallationLookup>();
            services.AddSingleton(provider =>
            {
                var clientFactory = provider.GetRequiredService<IGitHubClientFactory>();
                return clientFactory.CreateGitHubClient(provider
                    .GetRequiredService<IOptions<GitHubAccess>>().Value.GitHubAccessToken);
            });
        }

        public static Func<IServiceProvider, IGitHubClient> Client(IServiceCollection services)
        {
            return s => s.GetService<IGitHubClient>();
        }

        public static Func<IServiceProvider, IGitHubIssuesService> GitHubIssuesProvider(IServiceCollection services)
        {
            services.AddSingleton<IGitHubIssuesService, GitHubIssuesProvider>();
            return s => s.GetService<IGitHubIssuesService>();
        }

        private static (string assemblyName, string assemblyVersion) GetAssemblyVersion()
        {
            string assemblyName = Assembly.GetEntryAssembly()?.GetName().Name ?? "PostDeploymentTest";
            string assemblyVersion = Assembly.GetEntryAssembly()
                ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? "42.42.42.42";
            return (assemblyName, assemblyVersion);
        }
    }

    [Test]
    public async Task UpdateIssueBodyDoNotRemoveMilestoneTest()
    {
        TestData testData = await TestData.Default.BuildAsync();
        IGitHubClient client = testData.Client;
        IGitHubIssuesService gitHubIssuesProvider = testData.GitHubIssuesProvider;

        string organization = "maestro-auth-test";
        string repository = "build-result-analysis-test";
        string repositoryOwner = "maestro-auth-test/build-result-analysis-test";

        Issue issue = await CreateTestIssue(client, organization, repository);

        try
        {
            string body = @$"This issue is part of a post deployment test. Last updated in Utc time: {DateTimeOffset.UtcNow}";
            await gitHubIssuesProvider.UpdateIssueBodyAsync(repositoryOwner, issue.Number, body);

            Issue issueAfterIUpdate = await gitHubIssuesProvider.GetIssueAsync(repositoryOwner, issue.Number);
            issueAfterIUpdate.Milestone.Should().NotBeNull();
        }
        finally
        {
            TestContext.WriteLine("Cleaning up test by closing issue and removing milestone");
            await client.Issue.Update(organization, repository, issue.Number, new IssueUpdate {State = ItemState.Closed, Milestone = null});
        }
    }

    private async Task<Issue> CreateTestIssue(IGitHubClient client, string organization, string repository)
    {
        TestContext.WriteLine("Creating new issue to perform the test");

        var newIssue = new NewIssue("[POST DEPLOYMENT TEST ISSUE]")
        {
            Milestone = 1
        };

        Issue issue = await client.Issue.Create(organization, repository, newIssue);
        return issue;
    }

    public class GitHubAccess
    {
        public string GitHubAccessToken { get; set; }
    }
}
