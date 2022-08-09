using FluentAssertions;
using Microsoft.DotNet.Internal.AzureDevOps;
using Microsoft.DotNet.Internal.Testing.DependencyInjection.Abstractions;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DotNet.AzureDevOpsTimeline.Tests
{
    public partial class AzureDevOpsTimelineTimeoutTests
    {
        public static bool OperationCancelled = false;

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
                collection.Configure<AzureDevOpsTimelineOptions>((o, p) =>
                {
                    o.LogScrapingTimeout = "00:10:00";
                });
            }

            public static Func<IServiceProvider, AzureDevOpsTimeline> Controller(IServiceCollection collection)
            {
                collection.AddScoped<AzureDevOpsTimeline>();
                return s => s.GetRequiredService<AzureDevOpsTimeline>();
            }

            [ConfigureAllParameters]
            public static Func<IServiceProvider, InMemoryTimelineTelemetryRepository> Repository(
                IServiceCollection collection, string project, DateTimeOffset latestTime)
            {
                collection.AddSingleton<ITimelineTelemetryRepository>(
                    s => new InMemoryTimelineTelemetryRepository(
                        new List<(string project, DateTimeOffset latestTime)> { (project, latestTime) }
                    )
                );

                return s => (InMemoryTimelineTelemetryRepository)s.GetRequiredService<ITimelineTelemetryRepository>();
            }

            public static void Clock(IServiceCollection collection)
            {
                collection.AddSingleton<ISystemClock, TestClock>();
            }

            public static void Build(IServiceCollection collection, BuildAndTimeline build, DelegatingHandler delegatingHandler)
            {
                var httpClientHandler = new HttpClientHandler { CheckCertificateRevocationList = true };
                delegatingHandler.InnerHandler = httpClientHandler;
                collection.AddSingleton<IAzureDevOpsClient>(client => new MockTimeoutAzureClient(new Dictionary<Build, List<Timeline>>
                {
                    {build.Build, build.Timelines.ToList()}
                }, delegatingHandler));
                collection.AddSingleton<IBuildLogScraper, BuildLogScraper>();
            }
        }

        // The test uses the MockExceptionThrowingHandler to simulate a cancellationToken expiring
        [Test]
        public async Task TestAzureDevOpsTimelineTimeout()
        {
            DateTimeOffset timeDatum = DateTimeOffset.Parse("2021-03-04T05:00:00Z");
            string azdoProjectName = "public";
            string targetBranchName = "theTargetBranch";
            var delegatingHandler = MockExceptionThrowingDelegatingHandler.Create("Image: Build.Ubuntu.1804.Amd64", 1);

            BuildAndTimeline build = BuildAndTimelineBuilder.NewPullRequestBuild(1, azdoProjectName, targetBranchName)
                .AddTimeline(TimelineBuilder.EmptyTimeline("1", timeDatum)
                    .AddRecord("NetCore1ESPool-Internal 5", "Initialize job", "https://www.dev.azure.test")
                    .AddRecord("NetCore1ESPool-Internal 5", "Initialize job", "https://www.dev.azure.test"))
                .Build();

            // Test setup
            await using TestData testData = await TestData.Default
                .WithBuild(build)
                .WithDelegatingHandler(delegatingHandler)
                .BuildAsync();

            await testData.Controller.RunProject(azdoProjectName, 1000, CancellationToken.None);

            testData.Repository.TimelineRecords.Count(record => !string.IsNullOrEmpty(record.ImageName)).Should().Be(1);
            testData.Repository.TimelineRecords.Count(record => string.IsNullOrEmpty(record.ImageName)).Should().Be(1);
        }
    }

}
