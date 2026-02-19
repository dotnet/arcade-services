using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.Internal.Testing.DependencyInjection.Abstractions;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Helix.Test.Utilities;
using Microsoft.Internal.Helix.GitHub.Providers;
using Microsoft.Internal.Helix.GitHub.Services;
using Microsoft.Internal.Helix.KnownIssues.Providers;
using Microsoft.Internal.Helix.KnownIssues.Services;
using NUnit.Framework;
using Octokit;

namespace Microsoft.Internal.Helix.BuildResultAnalysisProcessor.Tests.PostDeploymentTests
{
    [TestFixture]
    [Category("SkipWhenLiveUnitTesting")]
    [Category("PostDeployment")]
    [Category("Staging")]
    public partial class GitHubCheckService_PostDeployment_Tests
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
                services.Configure<GiHubIssuesProviderPostDeploymentTests.GitHubAccess>("GitHubAccess", (o, s) => s.Bind(o));
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
                        .GetRequiredService<IOptions<GiHubIssuesProviderPostDeploymentTests.GitHubAccess>>().Value.GitHubAccessToken);
                });
            }

            public static Func<IServiceProvider, IGitHubChecksService> GitHubChecksProvider(IServiceCollection services)
            {
                services.AddSingleton<IGitHubChecksService, GitHubChecksProvider>();
                return s => s.GetService<IGitHubChecksService>();
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

        /// <summary>
        /// These tests use real repositories that we know are or are not supported by the Helix Int (Staging) GitHub App
        /// </summary>
        [TestCase("maestro-auth-test/build-result-analysis-test", true)]
        [TestCase("dotnet/arcade-services", true)]
        [TestCase("dotnet/fake-repo", false)]
        public async Task ValidateRepositoryIsSupported(string repository, bool expectedResult)
        {
            TestData testData = await TestData.Default.BuildAsync();

            IGitHubChecksService gitHubCheckService = testData.GitHubChecksProvider;
            bool isSupported = await gitHubCheckService.IsRepositorySupported(repository);

            isSupported.Should().Be(expectedResult);
        }
    }
}
