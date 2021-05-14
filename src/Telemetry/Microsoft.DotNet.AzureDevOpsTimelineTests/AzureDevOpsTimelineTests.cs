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

namespace Microsoft.DotNet.AzureDevOpsTimeline.Tests
{
    public class AzureDevOpsTimelineTests
    {
        /// <summary>
        /// Test a happy path. Database is empty, so all returned builds will be added.
        /// </summary>
        [Test]
        public async Task VanillaTest()
        {
            DateTimeOffset timeDatum = DateTimeOffset.Parse("2021-01-01T01:00:00Z");
            string azdoProjectName = "public";
            string targetBranchName = "theTargetBranch";

            BuildAndTimeline build = BuildAndTimelineBuilder.NewPullRequestBuild(1, azdoProjectName, targetBranchName)
                .AddTimeline(TimelineBuilder.EmptyTimeline("1", timeDatum)
                    .AddRecord()
                    .AddRecord()
                ).Build();

            // Test setup
            using TestData testData = new TestDataBuilder()
                .AddTelemetryRepository()
                .AddStaticClock(timeDatum)
                .AddBuildsWithTimelines(new List<BuildAndTimeline>() {
                    build
                }).Build();

            /// Test execution
            await testData.Controller.RunProject(azdoProjectName, 1000, CancellationToken.None);

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
            using TestData testData = new TestDataBuilder()
                .AddTelemetryRepository()
                .AddStaticClock(timeDatum)
                .AddBuildsWithTimelines(new List<BuildAndTimeline>() {
                    build
                }).Build();

            /// Test execution
            await testData.Controller.RunProject("public", 1000, CancellationToken.None);

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
            using TestData testData = new TestDataBuilder()
                .AddTelemetryRepository(azdoProjectName, timeDatum.AddHours(-1))
                .AddStaticClock(timeDatum)
                .AddBuildsWithTimelines(new List<BuildAndTimeline>() {
                    build
                }).Build();

            /// Test execution
            await testData.Controller.RunProject(azdoProjectName, 1000, CancellationToken.None);

            // Test results
            IEnumerable<TimelineRecord> expectedRecords = build.Timelines
                .Where(timeline => timeline.LastChangedOn > timeDatum.AddHours(-1))
                .SelectMany(b => b.Records);

            testData.Repository.TimelineRecords
                .Select(r => r.Raw)
                .Should().BeEquivalentTo(expectedRecords);
        }

        [Test]
        public async Task PullRequestWithNoProperties()
        {
            DateTimeOffset timeDatum = DateTimeOffset.Parse("2021-01-01T01:00:00Z");
            string azdoProjectName = "public";
            string targetBranchName = null;

            BuildAndTimeline build = BuildAndTimelineBuilder.NewPullRequestBuild(1, azdoProjectName, targetBranchName)
                .AddTimeline(TimelineBuilder.EmptyTimeline("1", timeDatum)
                    .AddRecord()
                    .AddRecord()
                ).Build();

            // Test setup
            using TestData testData = new TestDataBuilder()
                .AddTelemetryRepository()
                .AddStaticClock(timeDatum)
                .AddBuildsWithTimelines(new List<BuildAndTimeline>() {
                    build
                }).Build();

            /// Test execution
            await testData.Controller.RunProject(azdoProjectName, 1000, CancellationToken.None);

            // Test results
            testData.Repository.TimelineBuilds.Should().SatisfyRespectively(
                first =>
                {
                    first.Build.Should().BeSameAs(build.Build);
                    first.TargetBranch.Should().Be(string.Empty);
                });
        }
    }
}
