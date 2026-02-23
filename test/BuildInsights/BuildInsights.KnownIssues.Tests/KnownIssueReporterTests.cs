// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using AwesomeAssertions;
using BuildInsights.GitHubGraphQL;
using BuildInsights.GitHubGraphQL.GitHubGraphQLAPI;
using BuildInsights.KnownIssues.Models;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.DotNet.Internal.Logging;
using Microsoft.DotNet.Internal.Testing.DependencyInjection.Abstractions;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace BuildInsights.KnownIssues.Tests
{
    public partial class KnownIssueMonitorTests
    {
        public record struct GitHubIssuesServiceResult(List<string> Reports, Mock<IGitHubIssuesService> MockGitHubService);

        [TestDependencyInjectionSetup]
        public static class TestSetup
        {
            public static readonly DateTimeOffset DefaultTime = new DateTimeOffset(2001, 2, 3, 4, 5, 6, 7, TimeSpan.FromHours(-8)).UtcDateTime;

            public static void Defaults(IServiceCollection services)
            {
                services.AddLogging(l => l.AddProvider(new NUnitLogger()));
                services.AddSingleton(new TelemetryClient(new TelemetryConfiguration()));
                services.AddOperationTracking(o => o.ShouldStartActivity = false);
                services.AddSingleton<KnownIssuesReportHelper>();
                services.Configure<KnownIssuesProjectOptions>(o =>
                {
                    o.Organization = "TEST-ORGANIZATION";
                    o.ProjectNumber = 1;
                });
                services.Configure<GitHubIssuesSettings>(o =>
                {
                    o.KnownIssuesLabels = new List<string>() { "KNOWN-TEST-ERROR-ABC" };
                });
                services.Configure<SsaCriteriaSettings>(o =>
                {
                    o.DailyHitsForEscalation = 5;
                    o.SsaRepositories = ["REPOSITORY/OWNER-TEST"];
                    o.SsaLabel = "SSA_LABEL";
                });
            }

            public static Func<IServiceProvider, KnownIssueMonitor> Processor(IServiceCollection collection)
            {
                collection.AddSingleton<KnownIssueMonitor>();

                return s => s.GetRequiredService<KnownIssueMonitor>();
            }

            [ConfigureAllParameters]
            public static Func<IServiceProvider, Mock<IGitHubGraphQLClient>> ProjectIssuesData(
                IServiceCollection services,
                string body = "")
            {
                var projectItems = new List<GitHubGraphQLProjectV2Item>
                {
                    new() {
                        Id = "1",
                        IsArchived = false,
                        Content = new GitHubGraphQLIssue
                        {
                            Number = 1,
                            Title = "TEST-TITLE",
                            Body = body,
                            Closed = false,
                            CreatedAt = default,
                            Repository = new GitHubGraphQLRepository
                            {
                                Name = "TEST-REPOSITORY",
                                NameWithOwner = "REPOSITORY/OWNER-TEST"
                            },
                            Labels = new GitHubGraphQLLabels
                            {
                                Nodes = new List<GitHubGraphQLLabel>
                                {
                                    new() {
                                        Name = "KNOWN-TEST-ERROR-ABC"
                                    }
                                }
                            }
                        }
                    }
                };

                var mock = new Mock<IGitHubGraphQLClient>();
                mock.Setup(c => c.GetAllProjectIssues(It.IsAny<string>(), It.IsAny<int>()))
                    .ReturnsAsync(projectItems);

                services.AddSingleton(mock.Object);
                return _ => mock;
            }

            public static Func<IServiceProvider, Mock<IKnownIssuesService>> KnownIssueDataStore(
                IServiceCollection services, ImmutableList<KnownIssueMatch> knownIssueMatches = null,
                ImmutableList<TestKnownIssueMatch> testKnownIssueMatches = null)
            {
                knownIssueMatches ??= ImmutableList<KnownIssueMatch>.Empty;
                testKnownIssueMatches ??= ImmutableList<TestKnownIssueMatch>.Empty;

                var knownIssuesDataStoreMock = new Mock<IKnownIssuesService>();
                knownIssuesDataStoreMock
                    .Setup(d => d.GetKnownIssuesMatchesForIssue(It.IsAny<int>(), It.IsAny<string>()))
                    .ReturnsAsync(knownIssueMatches);
                knownIssuesDataStoreMock
                    .Setup(d => d.GetTestKnownIssuesMatchesForIssue(It.IsAny<int>(), It.IsAny<string>()))
                    .ReturnsAsync(testKnownIssueMatches);
                services.AddSingleton(knownIssuesDataStoreMock.Object);
                return _ => knownIssuesDataStoreMock;
            }

            public static Func<IServiceProvider, Mock<ISystemClock>> Clock(IServiceCollection services)
            {
                var mock = new Mock<ISystemClock>();
                mock.SetupGet(m => m.UtcNow).Returns(DefaultTime.UtcDateTime);
                services.AddSingleton(mock.Object);
                return _ => mock;
            }

            public static Func<IServiceProvider, GitHubIssuesServiceResult> UpdateIssueBody(IServiceCollection services)
            {
                Mock<IGitHubIssuesService> mock = new Mock<IGitHubIssuesService>();
                var reports = new List<string>();
                mock.Setup(
                        m => m.UpdateIssueBodyAsync(
                            It.IsAny<string>(),
                            It.IsAny<int>(),
                            It.IsAny<string>()
                        )
                    )
                    .Callback<string, int, string>(
                        (repository, issueId, body) =>
                        {
                            reports.Add(body);
                        }
                    )
                    .Returns(Task.CompletedTask);

                mock.Setup(g => g.AddLabelToIssueAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>())).Returns(Task.CompletedTask).Verifiable();

                services.AddSingleton(mock.Object);
                return _ => new GitHubIssuesServiceResult(reports, mock);
            }
        }

        [TestCase("")]
        [TestCase("First time having the report expecting to preserve previous body information")]
        public async Task WriteReportFirstTimeTest(string issueBody)
        {
            await using TestData testData = await TestData
                .Default
                .WithProjectIssuesData(issueBody)
                .BuildAsync();
            await testData.Processor.RunAsync();

            testData.UpdateIssueBody.Reports.Should().ContainSingle();

            string report = testData.UpdateIssueBody.Reports[0];

            report.Should().NotBeEmpty();
            report.Should().Contain(KnownIssueHelper.StartKnownIssueReportIdentifier, Exactly.Once());
            report.Should().Contain(KnownIssueHelper.EndKnownIssueReportIdentifier, Exactly.Once());
            if (!string.IsNullOrEmpty(issueBody))
            {
                report.Should().Contain(issueBody);
            }
        }

        [TestCase("Only having message before", "")]
        [TestCase("", "Only having message after")]
        [TestCase("Having message before", "And having message after")]
        public async Task WriteReportUpdateTest(string messageBeforeReport, string messageAfterReport)
        {
            string bodyReport = $"{KnownIssueHelper.StartKnownIssueReportIdentifier} report {KnownIssueHelper.EndKnownIssueReportIdentifier}";
            string issueBody = $"{messageBeforeReport} {bodyReport} {messageAfterReport}";

            await using TestData testData = await TestData.Default
                .WithProjectIssuesData(issueBody)
                .BuildAsync();
            await testData.Processor.RunAsync();

            testData.UpdateIssueBody.Reports.Should().ContainSingle();

            string report = testData.UpdateIssueBody.Reports[0];

            report.Should().NotBeEmpty();
            report.Should().Contain(KnownIssueHelper.StartKnownIssueReportIdentifier, Exactly.Once());
            report.Should().Contain(KnownIssueHelper.EndKnownIssueReportIdentifier, Exactly.Once());
            if (!string.IsNullOrEmpty(messageBeforeReport))
            {
                report.Should().Contain(messageBeforeReport);
            }
            if (!string.IsNullOrEmpty(messageAfterReport))
            {
                report.Should().Contain(messageAfterReport);
            }
        }

        [Test]
        public async Task TestCalculations()
        {
            List<KnownIssueMatch> knownIssueMatches =
            [
                MockKnownIssueMatch(1, TestSetup.DefaultTime.AddHours(1).UtcDateTime),
                MockKnownIssueMatch(2, TestSetup.DefaultTime.AddHours(1).UtcDateTime),
                MockKnownIssueMatch(3, TestSetup.DefaultTime.AddDays(-3).UtcDateTime),
                MockKnownIssueMatch(4, TestSetup.DefaultTime.AddDays(-4).UtcDateTime),
                MockKnownIssueMatch(5, TestSetup.DefaultTime.AddDays(-5).UtcDateTime),
                MockKnownIssueMatch(6, TestSetup.DefaultTime.AddDays(-10).UtcDateTime),
                MockKnownIssueMatch(7, TestSetup.DefaultTime.AddDays(-12).UtcDateTime),
                MockKnownIssueMatch(8, TestSetup.DefaultTime.AddDays(-15).UtcDateTime)
            ];

            await using TestData testData = await TestData.Default
                .WithKnownIssueMatches(knownIssueMatches.ToImmutableList())
                .BuildAsync();
            await testData.Processor.RunAsync();

            testData.UpdateIssueBody.Reports.Should().ContainSingle();

            string report = testData.UpdateIssueBody.Reports[0];

            //|Day Hit Count|Week Hit Count|Month Hit Count|
            report.Should().Contain("|2|5|8|");
        }

        [Test]
        public async Task TestKnownIssueCalculation()
        {
            List<TestKnownIssueMatch> knownIssueMatches =
            [
                MockTestKnownIssueMatch(1, TestSetup.DefaultTime.AddHours(1).UtcDateTime),
                MockTestKnownIssueMatch(2, TestSetup.DefaultTime.AddHours(1).UtcDateTime),
                MockTestKnownIssueMatch(3, TestSetup.DefaultTime.AddDays(-3).UtcDateTime),
                MockTestKnownIssueMatch(4, TestSetup.DefaultTime.AddDays(-4).UtcDateTime),
                MockTestKnownIssueMatch(5, TestSetup.DefaultTime.AddDays(-5).UtcDateTime),
                MockTestKnownIssueMatch(6, TestSetup.DefaultTime.AddDays(-10).UtcDateTime),
                MockTestKnownIssueMatch(7, TestSetup.DefaultTime.AddDays(-12).UtcDateTime),
                MockTestKnownIssueMatch(8, TestSetup.DefaultTime.AddDays(-15).UtcDateTime)
            ];

            await using TestData testData = await TestData.Default
                .WithTestKnownIssueMatches(knownIssueMatches.ToImmutableList())
                .BuildAsync();
            await testData.Processor.RunAsync();

            testData.UpdateIssueBody.Reports.Should().ContainSingle();

            string report = testData.UpdateIssueBody.Reports[0];

            //|Day Hit Count|Week Hit Count|Month Hit Count|
            report.Should().Contain("|2|5|8|");
        }

        [Test]
        public async Task MixKnownIssuesCalculation()
        {
            List<TestKnownIssueMatch> testKnownIssueMatches =
            [
                MockTestKnownIssueMatch(1, TestSetup.DefaultTime.AddHours(1).UtcDateTime),
                MockTestKnownIssueMatch(5, TestSetup.DefaultTime.AddDays(-5).UtcDateTime),
                MockTestKnownIssueMatch(7, TestSetup.DefaultTime.AddDays(-12).UtcDateTime)
            ];

            List<KnownIssueMatch> knownIssueMatches =
            [
                MockKnownIssueMatch(1, TestSetup.DefaultTime.AddHours(1).UtcDateTime),
                MockKnownIssueMatch(3, TestSetup.DefaultTime.AddDays(-3).UtcDateTime),
                MockKnownIssueMatch(8, TestSetup.DefaultTime.AddDays(-15).UtcDateTime)
            ];

            await using TestData testData = await TestData.Default
                .WithTestKnownIssueMatches(testKnownIssueMatches.ToImmutableList())
                .WithKnownIssueMatches(knownIssueMatches.ToImmutableList())
                .BuildAsync();
            await testData.Processor.RunAsync();

            testData.UpdateIssueBody.Reports.Should().ContainSingle();

            string report = testData.UpdateIssueBody.Reports[0];

            //|Day Hit Count|Week Hit Count|Month Hit Count|
            report.Should().Contain("|1|3|5|");
        }

        [Test]
        public async Task BuildShowedOnReport()
        {
            List<KnownIssueMatch> knownIssueMatches =
            [
                MockKnownIssueMatch(123456, TestSetup.DefaultTime.AddHours(1).DateTime),
                MockKnownIssueMatch(654321, TestSetup.DefaultTime.AddHours(1).DateTime),
                MockKnownIssueMatch(123654, TestSetup.DefaultTime.AddDays(-3).DateTime)
            ];

            await using TestData testData = await TestData.Default
                .WithKnownIssueMatches(knownIssueMatches.ToImmutableList())
                .BuildAsync();
            await testData.Processor.RunAsync();

            testData.UpdateIssueBody.Reports.Should().ContainSingle();

            string report = testData.UpdateIssueBody.Reports[0];

            report.Should().Contain("123456");
            report.Should().Contain("654321");
            report.Should().Contain("123654");
        }

        [Test]
        public async Task TestKnownIssuesShowedOnReport()
        {
            List<TestKnownIssueMatch> testKnownIssueMatches =
            [
                MockTestKnownIssueMatch(1, TestSetup.DefaultTime.AddHours(1).UtcDateTime, "Test-Name-A"),
                MockTestKnownIssueMatch(5, TestSetup.DefaultTime.AddDays(-5).UtcDateTime, "Test-Name-B"),
                MockTestKnownIssueMatch(7, TestSetup.DefaultTime.AddDays(-12).UtcDateTime, "Test-Name-C")
            ];

            await using TestData testData = await TestData.Default
                .WithTestKnownIssueMatches(testKnownIssueMatches.ToImmutableList())
                .BuildAsync();
            await testData.Processor.RunAsync();

            testData.UpdateIssueBody.Reports.Should().ContainSingle();

            string report = testData.UpdateIssueBody.Reports[0];

            report.Should().Contain("|1|BUILD-REPOSITORY|[Test-Name-A](TEST-RUN-URL)|BUILD-REPOSITORY#123|");
            report.Should().Contain("|5|BUILD-REPOSITORY|[Test-Name-B](TEST-RUN-URL)|BUILD-REPOSITORY#123|");
            report.Should().Contain("|7|BUILD-REPOSITORY|[Test-Name-C](TEST-RUN-URL)|BUILD-REPOSITORY#123|");
        }

        [Test]
        public void GetBuildLinkNoProject()
        {
            string link = KnownIssueMonitor.GetBuildLink(MockKnownIssueMatch(1234, TestSetup.DefaultTime.DateTime));
            link.Should().Be("1234");
        }

        [Test]
        public void GetBuildLinkWithProject()
        {
            var mockMatch = MockKnownIssueMatch(1234, TestSetup.DefaultTime.DateTime);
            mockMatch.Project = "public";
            string link = KnownIssueMonitor.GetBuildLink(mockMatch);
            link.Should().Be("[1234](https://dev.azure.com/dnceng/public/_build/results?buildId=1234)");
        }


        [TestCase(15, 1)]
        [TestCase(1, 0)]
        public async Task AddSsaLabelToIssueAfterMeetingHitsCriteriaTest(int issues, int expectedRuns)
        {
            var knownIssueMatches = new List<KnownIssueMatch>();

            for (int i = 0; i < issues; i++)
            {
                knownIssueMatches.Add(MockKnownIssueMatch(i, TestSetup.DefaultTime.AddHours(1).UtcDateTime));
            }

            await using TestData testData = await TestData.Default
                .WithKnownIssueMatches(knownIssueMatches.ToImmutableList())
                .BuildAsync();
            await testData.Processor.RunAsync();

            testData.UpdateIssueBody.MockGitHubService.Verify(
                g => g.AddLabelToIssueAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()),
                Times.Exactly(expectedRuns));
        }

        [Test]
        public async Task GetPullRequestLinkForInternal()
        {
            KnownIssueMatch mockMatch = MockKnownIssueMatch(1234, TestSetup.DefaultTime.DateTime);
            mockMatch.Project = "internal";
            mockMatch.PullRequest = "1234";
            mockMatch.Organization = "testOrg";
            mockMatch.BuildRepository = "testRepository";

            await using TestData testData = await TestData.Default
                .WithKnownIssueMatches(ImmutableList.Create(mockMatch))
                .BuildAsync();
            await testData.Processor.RunAsync();

            string report = testData.UpdateIssueBody.Reports[0];
            report.Should()
                .Contain("[#1234](https://dev.azure.com/testOrg/internal/_git/testRepository/pullrequest/1234)");
        }

        private static KnownIssueMatch MockKnownIssueMatch(int buildId, DateTime reported)
        {
            return new KnownIssueMatch
            {
                BuildId = buildId,
                BuildRepository = "BUILD-REPOSITORY",
                IssueId = 1,
                IssueRepository = "TEST-REPOSITORY",
                JobId = "TEST-JOB-ID",
                StepName = "TEST-STEP-NAME-ABC",
                StepStartTime = reported,
                Organization = "dnceng"
            };
        }

        private static TestKnownIssueMatch MockTestKnownIssueMatch(int buildId, DateTime reported, string testName = null)
        {
            return new TestKnownIssueMatch
            {
                BuildId = buildId,
                BuildRepository = "BUILD-REPOSITORY",
                IssueId = 1,
                IssueRepository = "TEST-REPOSITORY",
                IssueType = "TEST",
                TestResultName = testName ?? "TEST-NAME",
                TestRunId = 1,
                PullRequest = "123",
                Url = "TEST-RUN-URL",
                CompletedDate = reported
            };
        }
    }
}
