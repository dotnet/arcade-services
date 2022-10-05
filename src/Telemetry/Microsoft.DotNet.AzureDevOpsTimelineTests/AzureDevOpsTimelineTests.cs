// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using FluentAssertions;
using Microsoft.DotNet.Internal.AzureDevOps;
using Microsoft.DotNet.Internal.Testing.Utility;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Internal.DependencyInjection;
using Microsoft.DotNet.Internal.Testing.DependencyInjection.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Moq;

namespace Microsoft.DotNet.AzureDevOpsTimeline.Tests;

public partial class AzureDevOpsTimelineTests
{
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
                o.LogScrapingTimeout = "00:00:02";
            });
        }

        public static Func<IServiceProvider,AzureDevOpsTimeline> Controller(IServiceCollection collection)
        {
            collection.AddScoped<AzureDevOpsTimeline>();
            return s => s.GetRequiredService<AzureDevOpsTimeline>();
        }

        [ConfigureAllParameters]
        public static Func<IServiceProvider, InMemoryTimelineTelemetryRepository> Repository(
            IServiceCollection collection, string project, string organization, DateTimeOffset latestTime)
        {
            collection.AddSingleton<ITimelineTelemetryRepository>(
                s => new InMemoryTimelineTelemetryRepository(
                    new List<(string project, string organization, DateTimeOffset latestTime)> {(project, organization, latestTime)}
                )
            );

            return s => (InMemoryTimelineTelemetryRepository) s.GetRequiredService<ITimelineTelemetryRepository>();
        }

        public static void Clock(IServiceCollection collection)
        {
            collection.AddSingleton<ISystemClock, TestClock>();
        }

        public static void Build(IServiceCollection collection, BuildAndTimeline build)
        {
            collection.AddSingleton<IAzureDevOpsClient>(client => new MockAzureClient(new Dictionary<Build, List<Timeline>>
            {
                {build.Build, build.Timelines.ToList()}
            }));
            collection.AddSingleton<IClientFactory<IAzureDevOpsClient>>(provider =>
                new SingleClientFactory<IAzureDevOpsClient>(provider.GetRequiredService<IAzureDevOpsClient>()));
            collection.AddSingleton<IBuildLogScraper, BuildLogScraper>();
        }
    }

    /// <summary>
    /// Test a happy path. Database is empty, so all returned builds will be added.
    /// </summary>
    [Test]
    public async Task VanillaTest()
    {
        DateTimeOffset timeDatum = DateTimeOffset.Parse("2021-01-01T01:00:00Z");
        string azdoProjectName = "public";
        string azdoOrganizationName = "dnceng";
        string targetBranchName = "theTargetBranch";

        BuildAndTimeline build = BuildAndTimelineBuilder.NewPullRequestBuild(1, azdoProjectName, targetBranchName)
            .AddTimeline(TimelineBuilder.EmptyTimeline("1", timeDatum)
                .AddRecord()
                .AddRecord()
            ).Build();

        // Test setup
        await using TestData testData = await TestData.Default
            .WithBuild(build)
            .BuildAsync();

        /// Test execution
        await testData.Controller.RunProject(
            new AzureDevOpsProject {Project = azdoProjectName, Organization = azdoOrganizationName},
            1000,
            CancellationToken.None);

        // Test results
        testData.Repository.TimelineBuilds.Should().SatisfyRespectively(
            first =>
            {
                first.Build.Should().BeSameAs(build.Build);
                first.TargetBranch.Should().Be(targetBranchName);
            });

        testData.Repository.TimelineIssues.Should().BeEmpty();

        testData.Repository.TimelineRecords.Should().HaveCount(2);
        testData.Repository.TimelineRecords
            .Select(r => r.Raw)
            .Should().Contain(build.Timelines
                .SelectMany(timeline => timeline.Records));

        testData.Repository.TimelineValidationMessages.Should().BeEmpty();
    }

    /// <summary>
    /// Build has multiple timelines. 
    /// </summary>
    /// <returns></returns>
    [Test]
    public async Task GetAdditionalTimelines()
    {
        DateTimeOffset timeDatum = DateTimeOffset.Parse("2021-01-01T01:00:00Z");
        string azdoProjectName = "public";
        string azdoOrganizationName = "dnceng";
        string targetBranchName = "theTargetBranch";

        // Test input
        BuildAndTimeline build = BuildAndTimelineBuilder.NewPullRequestBuild(1, azdoProjectName, targetBranchName)
            .AddTimeline(TimelineBuilder.EmptyTimeline("1", timeDatum)
                .AddRecord()
                .AddRecord("2")) // Represents a retry
            .AddTimeline(TimelineBuilder.EmptyTimeline("2", timeDatum.AddHours(-1))
                .AddRecord()
                .AddRecord())
            .Build();

        // Test setup
        using TestData testData = await TestData.Default
            .WithBuild(build)
            .BuildAsync();

        /// Test execution
        await testData.Controller.RunProject(new AzureDevOpsProject
        {
            Project = azdoProjectName,
            Organization = azdoOrganizationName,
        }, 1000, CancellationToken.None);
            
        // Test results
        testData.Repository.TimelineRecords
            .Select(r => r.Raw)
            .Should()
            .BeEquivalentTo(build.Timelines
                .SelectMany(timeline => timeline.Records));

        testData.Repository.TimelineValidationMessages.Should().BeEmpty();
    }

    /// <summary>
    /// Build has multiple timelines, but one timeline is older than the cutoff and should be ignored. 
    /// </summary>
    [Test]
    public async Task AdditionalTimelines_IgnoreOldest()
    {
        // Test input
        DateTimeOffset timeDatum = DateTimeOffset.Parse("2021-01-01T01:00:00Z");
        string azdoProjectName = "public";
        string azdoOrganizationName = "dnceng";
        string targetBranchName = "theTargetBranch";

        BuildAndTimeline build = BuildAndTimelineBuilder.NewPullRequestBuild(1, azdoProjectName, targetBranchName)
            .AddTimeline(TimelineBuilder.EmptyTimeline("1", timeDatum)
                .AddRecord()
                .AddRecord("2")) // Represents a retry
            .AddTimeline(TimelineBuilder.EmptyTimeline("2", timeDatum.AddHours(-1))
                .AddRecord()
                .AddRecord())
            .Build();

        // Test setup
        await using TestData testData = await TestData.Default
            .WithRepository(azdoProjectName, azdoOrganizationName, timeDatum.AddHours(-1))
            .WithBuild(build)
            .BuildAsync();

        /// Test execution
        await testData.Controller.RunProject(
            new AzureDevOpsProject {Project = azdoProjectName, Organization = azdoOrganizationName},
            1000,
            CancellationToken.None);

        // Test results
        IEnumerable<TimelineRecord> expectedRecords = build.Timelines
            .Where(timeline => timeline.LastChangedOn > timeDatum.AddHours(-1))
            .SelectMany(b => b.Records);

        testData.Repository.TimelineRecords
            .Select(r => r.Raw)
            .Should().BeEquivalentTo(expectedRecords);
    }

    [Test]
    public async Task PullRequestWithNoParameters()
    {
        DateTimeOffset timeDatum = DateTimeOffset.Parse("2021-01-01T01:00:00Z");
        string azdoProjectName = "public";
        string azdoOrganizationName = "dnceng";
        string targetBranchName = null;

        BuildAndTimeline build = BuildAndTimelineBuilder.NewPullRequestBuild(1, azdoProjectName, targetBranchName)
            .AddTimeline(TimelineBuilder.EmptyTimeline("1", timeDatum)
                .AddRecord()
                .AddRecord()
            ).Build();

        // Test setup
        await using TestData testData = await TestData.Default
            .WithBuild(build)
            .BuildAsync();

        /// Test execution
        await testData.Controller.RunProject(
            new AzureDevOpsProject {Project = azdoProjectName, Organization = azdoOrganizationName},
            1000,
            CancellationToken.None);

        // Test results
        testData.Repository.TimelineBuilds.Should().SatisfyRespectively(
            first =>
            {
                first.Build.Should().BeSameAs(build.Build);
                first.TargetBranch.Should().Be(string.Empty);
            });
    }

    [Test]
    public async Task BuildLogScraping()
    {
        DateTimeOffset timeDatum = DateTimeOffset.Parse("2021-01-01T01:00:00Z");
        string azdoProjectName = "public";
        string azdoOrganizationName = "dnceng";
        string targetBranchName = "theTargetBranch";

        BuildAndTimeline build = BuildAndTimelineBuilder.NewPullRequestBuild(1, azdoProjectName, targetBranchName)
            .AddTimeline(TimelineBuilder.EmptyTimeline("1", timeDatum)
                .AddRecord("NetCore1ESPool-Internal 5", "Initialize job", MockAzureClient.OneESLogUrl)
                .AddRecord("Azure Pipelines", "Initialize job", MockAzureClient.MicrosoftHostedAgentLogUrl)
                .AddRecord("Azure Pipelines", "Initialize containers", MockAzureClient.DockerLogUrl)
                .AddRecord()
            ).Build();

        // Test setup
        await using TestData testData = await TestData.Default
            .WithBuild(build)
            .BuildAsync();

        /// Test execution
        await testData.Controller.RunProject(
            new AzureDevOpsProject {Project = azdoProjectName, Organization = azdoOrganizationName},
            1000,
            CancellationToken.None);

        testData.Repository.TimelineRecords.Count(record => !string.IsNullOrEmpty(record.ImageName)).Should().Be(3);
        testData.Repository.TimelineRecords.Count(record => string.IsNullOrEmpty(record.ImageName)).Should().Be(1);
    }
}
