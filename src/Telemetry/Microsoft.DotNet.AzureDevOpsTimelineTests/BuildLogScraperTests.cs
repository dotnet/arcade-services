using Microsoft.DotNet.Internal.AzureDevOps;
using Microsoft.DotNet.Internal.Testing.DependencyInjection.Abstractions;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DotNet.AzureDevOpsTimeline.Tests
{
    public partial class BuildLogScraperTests
    {
        public static string EmptyUrl = "https://www.fakeurl.test";

        [TestDependencyInjectionSetup]
        public static class TestDataConfiguration
        {
            public static void Dependencies(IServiceCollection collection)
            {
                collection.AddOptions();
                collection.AddLogging(logging =>
                {
                    logging.AddProvider(new NUnitLogger());
                });
            }

            public static Func<IServiceProvider, BuildLogScraper> Controller(IServiceCollection collection)
            {
                collection.AddScoped<BuildLogScraper>();
                return s => s.GetRequiredService<BuildLogScraper>();
            }

            public static void Build(IServiceCollection collection, (string url, string content) mockRequest)
            {
                var mockHttpClientFactory = new MockHttpClientFactory();
                mockHttpClientFactory.AddCannedResponse(mockRequest.url, mockRequest.content);
                collection.AddSingleton<IHttpClientFactory>(mockHttpClientFactory);
                collection.AddSingleton(ExponentialRetry.Default);
                collection.AddSingleton(new AzureDevOpsClientOptions
                {
                    MaxParallelRequests = 2
                });
                var options = new AzureDevOpsClientOptions
                {
                    MaxParallelRequests = 2
                };
                collection.AddSingleton<IOptions<AzureDevOpsClientOptions>>(Options.Create(options));
                collection.AddSingleton<IAzureDevOpsClient, AzureDevOpsClient>();
            }
        }

        [Test]
        public async Task BuildLogScraperShouldExtractMicrosoftHostedPoolImageName()
        {
            await using TestData testData = await TestData.Default
                .WithMockRequest((MockAzureClient.OneESLogUrl, MockAzureClient.OneESLog))
                .BuildAsync();

            var imageName = await testData.Controller.ExtractOneESHostedPoolImageNameAsync(
                MockAzureClient.OneESLogUrl,
                CancellationToken.None);
            Assert.AreEqual(MockAzureClient.OneESImageName, imageName);
        }

        [Test]
        public async Task BuildLogScraperShouldExtractOneESHostedPoolImageName()
        {
            await using TestData testData = await TestData.Default
                .WithMockRequest((MockAzureClient.MicrosoftHostedAgentLogUrl, MockAzureClient.MicrosoftHostedLog))
                .BuildAsync();

            var imageName = await testData.Controller.ExtractMicrosoftHostedPoolImageNameAsync(
                MockAzureClient.MicrosoftHostedAgentLogUrl,
                CancellationToken.None);
            Assert.AreEqual(MockAzureClient.MicrosoftHostedAgentImageName, imageName);
        }

        [Test]
        public async Task BuildLogScraperShouldExtractDockerImageName()
        {
            await using TestData testData = await TestData.Default
                .WithMockRequest((MockAzureClient.DockerLogUrl, MockAzureClient.DockerLog))
                .BuildAsync();

            var imageName = await testData.Controller.ExtractDockerImageNameAsync(
                MockAzureClient.DockerLogUrl,
                CancellationToken.None);
            Assert.AreEqual(MockAzureClient.DockerImageName, imageName);
        }

        [Test]
        public async Task BuildLogScraperShouldntExtractAnything()
        {
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

            await using TestData testData = await TestData.Default
                .WithMockRequest((EmptyUrl, string.Empty))
                .BuildAsync();

            Assert.IsNull(await testData.Controller.ExtractOneESHostedPoolImageNameAsync(EmptyUrl, cancellationTokenSource.Token));
            Assert.IsNull(await testData.Controller.ExtractMicrosoftHostedPoolImageNameAsync(EmptyUrl, cancellationTokenSource.Token));

        }
    }
}
