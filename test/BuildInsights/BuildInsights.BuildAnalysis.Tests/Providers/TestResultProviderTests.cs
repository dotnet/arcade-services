// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using System.Net;
using System.Text;
using AwesomeAssertions;
using BuildInsights.BuildAnalysis.Models;
using BuildInsights.KnownIssues;
using BuildInsights.KnownIssues.Models;
using Microsoft.DotNet.Internal.Testing.DependencyInjection.Abstractions;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using NUnit.Framework;
using Octokit;

namespace BuildInsights.BuildAnalysis.Tests.Providers
{
    [TestFixture]
    public partial class TestResultProviderTests
    {
        [TestDependencyInjectionSetup]
        public static class TestSetup
        {
            public static void Defaults(IServiceCollection collection)
            {
                collection.AddLogging(l => l.AddProvider(new NUnitLogger()));
                collection.Configure<KnownIssuesAnalysisLimits>(
                    l =>
                    {
                        l.RecordCountLimit = 1000;
                        l.LogLinesCountLimit = 100000;
                        l.FailingTestCountLimit = 1000;
                        l.HelixLogsFilesLimit = 100;
                    }
                );

                collection.AddSingleton<IKnownIssuesMatchService, KnownIssuesMatchProvider>();
            }

            public static Func<IServiceProvider, TestResultProvider> Provider(IServiceCollection collection)
            {
                collection.AddSingleton<TestResultProvider>();

                return s => s.GetRequiredService<TestResultProvider>();
            }

            public static Func<IServiceProvider, Mock<IHelixDataService>> HelixDataService(IServiceCollection services,
                Dictionary<string, List<HelixWorkItem>> testHelixWorkItems)
            {
                testHelixWorkItems ??= [];
                Mock<IHelixDataService> helixDataService = new Mock<IHelixDataService>();
                helixDataService.Setup(h =>
                        h.TryGetHelixWorkItems(It.IsAny<ImmutableList<string>>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(testHelixWorkItems);
                helixDataService.Setup(h => h.IsHelixWorkItem(It.IsAny<string>())).Returns(true);
                services.AddSingleton(helixDataService.Object);
                return _ => helixDataService;
            }

            public static Func<IServiceProvider, Mock<IHttpClientFactory>> HttpClientFactory(
                IServiceCollection services, Stream streamResult)
            {
                streamResult ??= new MemoryStream();
                Mock<HttpMessageHandler> httpMessageHandlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
                httpMessageHandlerMock.Protected()
                    .Setup<Task<HttpResponseMessage>>("SendAsync",
                        ItExpr.Is<HttpRequestMessage>(rm =>
                            rm.RequestUri.AbsoluteUri.StartsWith("https://helix.test/")),
                        ItExpr.IsAny<CancellationToken>())
                    .ReturnsAsync(new HttpResponseMessage
                    {
                        StatusCode = HttpStatusCode.OK,
                        Content = new StreamContent(streamResult)
                    });

                Mock<IHttpClientFactory> httpClientFactory = new Mock<IHttpClientFactory>();
                httpClientFactory.Setup(m => m.CreateClient(It.IsAny<string>()))
                    .Returns(new HttpClient(httpMessageHandlerMock.Object)); // lgtm [cs/httpclient-checkcertrevlist-disabled] Used only for unit testing

                services.AddSingleton(httpClientFactory.Object);
                return _ => httpClientFactory;
            }
        }

        [Test]
        public async Task GetTestFailingWithKnownIssuesAnalysisTestsWithManyTestFailures()
        {

            List<TestCaseResult> testCaseResults = [];
            for (int i = 0; i < 1200; i++)
                testCaseResults.Add(MockTestCaseResult($"{i} - Test", ""));

            List<TestRunDetails> failingTestCaseResults =
            [
                new TestRunDetails(MockTestRunSummary(),testCaseResults,DateTimeOffset.MaxValue)
            ];

            await using TestData testData = await TestData.Default.BuildAsync();
            TestKnownIssuesAnalysis result =
                await testData.Provider.GetTestFailingWithKnownIssuesAnalysis(failingTestCaseResults, new List<KnownIssue>(), "test-org");
            result.IsAnalysisAvailable.Should().BeFalse();
            result.TestResultWithKnownIssues.Should().HaveCount(0);
        }

        [Test]
        public async Task GetTestFailingWithKnownIssuesAnalysisFromHelixLog()
        {
            string comment =
                "{\r\n  \"HelixJobId\": \"abc-def-ghi\",\r\n  \"HelixWorkItemName\": \"TestHelixWorkItemNameCommentA\"\r\n}";

            var testHelixWorkItems = new Dictionary<string, List<HelixWorkItem>>
            {
                {
                    comment, new List<HelixWorkItem>
                    {
                        new()
                        {
                            HelixJobId = "abc-def-ghi",
                            HelixWorkItemName = "TestHelixWorkItemNameCommentA",
                            ConsoleLogUrl = "https://helix.test/TestConsoleLogUrl",
                            ExitCode = 0
                        }
                    }
                }
            };

            List<TestCaseResult> testCaseResults = [MockTestCaseResult("TestABC", comment)];
            List<TestRunDetails> failingTestCaseResults =
            [
                new TestRunDetails(MockTestRunSummary(),testCaseResults,DateTimeOffset.MaxValue)
            ];

            List<KnownIssue> knownIssues =
            [
                KnownIssueHelper.ParseGithubIssue(MockGithubIssue("```\r\n{\r\n    \"errorMessage\" : \"ran longer than the maximum time\"\r\n}\r\n```"), "test-repo", KnownIssueType.Test)
            ];

            MemoryStream memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(@"ran longer than the maximum time"));

            await using TestData testData = await TestData.Default.WithTestHelixWorkItems(testHelixWorkItems)
                .WithStreamResult(memoryStream).BuildAsync();
            var result = await testData.Provider.GetTestFailingWithKnownIssuesAnalysis(failingTestCaseResults, knownIssues, "test-org");
            result.IsAnalysisAvailable.Should().BeTrue();
            result.TestResultWithKnownIssues.Should().HaveCount(1);
            result.TestResultWithKnownIssues.First().TestCaseResult.Name.Should().Be("TestABC");
            result.TestResultWithKnownIssues.First().KnownIssues.First().BuildError.First().Should()
                .Be("ran longer than the maximum time");
        }

        [Test]
        public async Task GetTestFailingWithKnownIssuesAnalysisExcludeHelixLog()
        {
            string comment =
                "{\r\n  \"HelixJobId\": \"abc-def-ghi\",\r\n  \"HelixWorkItemName\": \"TestHelixWorkItemNameCommentA\"\r\n}";

            var testHelixWorkItems = new Dictionary<string, List<HelixWorkItem>>
            {
                {
                    comment, new List<HelixWorkItem>
                    {
                        new()
                        {
                            HelixJobId = "abc-def-ghi",
                            HelixWorkItemName = "TestHelixWorkItemNameCommentA",
                            ConsoleLogUrl = "https://helix.test/TestConsoleLogUrl",
                            ExitCode = 0
                        }
                    }
                }
            };

            List<TestCaseResult> testCaseResults = [MockTestCaseResult("TestABC", comment)];
            List<TestRunDetails> failingTestCaseResults =
            [
                new TestRunDetails(MockTestRunSummary(),testCaseResults,DateTimeOffset.MaxValue)
            ];

            List<KnownIssue> knownIssues =
            [
                KnownIssueHelper.ParseGithubIssue(MockGithubIssue("```\r\n{\r\n    \"errorMessage\" : \"ran longer than the maximum time\",\r\n    \"ExcludeConsoleLog\": true\r\n}\r\n```"), "test-repo", KnownIssueType.Test)
            ];

            MemoryStream memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(@"ran longer than the maximum time"));

            await using TestData testData = await TestData.Default.WithTestHelixWorkItems(testHelixWorkItems)
                .WithStreamResult(memoryStream).BuildAsync();
            var result = await testData.Provider.GetTestFailingWithKnownIssuesAnalysis(failingTestCaseResults, knownIssues, "test-org");
            result.IsAnalysisAvailable.Should().BeTrue();
            result.TestResultWithKnownIssues.Should().HaveCount(0);
        }

        [Test]
        public async Task GetTestFailingWithKnownIssuesAnalysisFromTestErrorMessage()
        {
            string comment = "{\r\n  \"HelixJobId\": \"abc-def-ghi\",\r\n  \"HelixWorkItemName\": \"TestHelixWorkItemNameCommentA\"\r\n}";
            string testErrorMessage = "ran longer than the maximum time";

            List<TestCaseResult> testCaseResults = [MockTestCaseResult("TestABC", comment, testErrorMessage)];
            List<TestRunDetails> failingTestCaseResults =
            [
                new TestRunDetails(MockTestRunSummary(),testCaseResults,DateTimeOffset.MaxValue)
            ];

            List<KnownIssue> knownIssues = [
                KnownIssueHelper.ParseGithubIssue(
                    MockGithubIssue("```\r\n{\r\n    \"errorMessage\" : \"ran longer than the maximum time\"\r\n}\r\n```"),
                    "test-repository",
                    KnownIssueType.Test)
            ];


            await using TestData testData = await TestData.Default.BuildAsync();
            var result = await testData.Provider.GetTestFailingWithKnownIssuesAnalysis(failingTestCaseResults, knownIssues, "test-org");
            result.IsAnalysisAvailable.Should().BeTrue();
            result.TestResultWithKnownIssues.Should().HaveCount(1);
            result.TestResultWithKnownIssues.First().TestCaseResult.Name.Should().Be("TestABC");
            result.TestResultWithKnownIssues.First().KnownIssues.First().BuildError.First().Should()
                .Be("ran longer than the maximum time");
        }

        private TestCaseResult MockTestCaseResult(string name, string comment, string errorMessage = "")
        {
            return new TestCaseResult(name, MockDateTimeOffset(),
                TestOutcomeValue.Failed, 0, 0, 0, new PreviousBuildRef(), errorMessage, "", "", comment, 55000);
        }

        private Issue MockGithubIssue(string body)
        {
            return new Issue(default, default, default, default, default, ItemState.Open, default, body, default,
                default, default, default, default, default, 1, default, DateTimeOffset.MaxValue, DateTimeOffset.MaxValue,
                DateTimeOffset.MaxValue, 1, default, default, default, default, default, default);
        }

        private TestRunSummary MockTestRunSummary()
        {
            return new TestRunSummary(1, "TestRunSummar", new PipelineReference(
                new StageReference("__deafult", 1),
                new PhaseReference("__deafult", 1),
                new JobReference("__deafult", 1)));
        }

        private static DateTimeOffset MockDateTimeOffset()
        {
            return new DateTimeOffset(2001, 2, 3, 4, 5, 6, TimeSpan.Zero);
        }
    }
}
