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
                    o.LogScrapingTimeout = "00:00:00.100";
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

            public static void Clock(IServiceCollection collection, DateTimeOffset staticClock)
            {
                Mock<ISystemClock> mockSystemClock = new Mock<ISystemClock>();
                mockSystemClock.Setup(x => x.UtcNow).Returns(staticClock);
                collection.AddSingleton(mockSystemClock.Object);
            }

            public static void Build(IServiceCollection collection, BuildAndTimeline build, HttpMessageHandler httpMessageHandler)
            {
                collection.AddSingleton<IAzureDevOpsClient>(client => new MockTimeoutAzureClient(new Dictionary<Build, List<Timeline>>
                {
                    {build.Build, build.Timelines.ToList()}
                }, httpMessageHandler));
                collection.AddSingleton<IBuildLogScraper, BuildLogScraper>();
            }
        }

        [Test]
        public async Task TestAzureDevOpsTimelineTimeout()
        {
            DateTimeOffset timeDatum = DateTimeOffset.Parse("2021-01-01T01:00:00Z");
            string azdoProjectName = "public";
            string targetBranchName = "theTargetBranch";
            MockHttpMessageHandler httpMessageHandler = MockHttpMessageHandler.Create("some string");

            BuildAndTimeline build = BuildAndTimelineBuilder.NewPullRequestBuild(1, azdoProjectName, targetBranchName)
                .AddTimeline(TimelineBuilder.EmptyTimeline("1", timeDatum)
                    .AddRecord("NetCore1ESPool-Internal 5", "Initialize job", "https://www.microsoft.com"))
                .Build();

            // Test setup
            await using TestData testData = await TestData.Default
                .WithStaticClock(timeDatum)
                .WithBuild(build)
                .WithHttpMessageHandler(httpMessageHandler)
                .BuildAsync();

            await testData.Controller.RunProject(azdoProjectName, 1000, CancellationToken.None);

            testData.Repository.TimelineRecords.Count(record => string.IsNullOrEmpty(record.ImageName)).Should().Be(1);
            httpMessageHandler.RequestCancelledCount.Should().Be(1);
        }
    }

}
