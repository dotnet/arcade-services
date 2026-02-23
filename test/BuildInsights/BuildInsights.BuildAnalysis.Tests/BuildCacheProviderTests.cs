using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using BuildInsights.BuildAnalysis.Models;
using BuildInsights.GitHub.Models;
using BuildInsights.KnownIssues.Models;
using BuildInsights.AzureStorage.Cache;
using NUnit.Framework;

namespace BuildInsights.BuildAnalysis.Tests
{
    [TestFixture]
    public class BuildCacheProviderTests
    {
        public sealed class TestData : IDisposable, IAsyncDisposable
        {
            private readonly ServiceProvider _services;

            private TestData(
                ServiceProvider services)
            {
                _services = services;
            }

            public BuildCacheProvider BuildCacheProvider => _services.GetRequiredService<BuildCacheProvider>();

            public class Builder
            {
                public Builder()
                {
                }

                public TestData Build()
                {
                    var collection = new ServiceCollection();

                    var storage = new MockContextualStorage();
                    storage.SetContext("TEST-CONTEXT");
                    collection.AddSingleton<IContextualStorage>(storage);
                    collection.AddSingleton<BuildCacheProvider>();

                    ServiceProvider service = collection.BuildServiceProvider();
                    return new TestData(service);
                }
            }

            public static Builder Create() => new Builder();
            public static TestData BuildDefault() => Create().Build();

            public void Dispose()
            {
                _services.Dispose();
            }

            public ValueTask DisposeAsync()
            {
                return _services.DisposeAsync();
            }
        }

        [Test]
        public async Task RoundtripIsEquatable()
        {
            var buildRef = new BuildReferenceIdentifier(
                org: "dnceng-public",
                project: "public",
                buildId: 12345,
                buildUrl: "any.example.url",
                definitionId: 1,
                definitionName:"FAKE-DEFINITION-NAME",
                repositoryId: "fake/repo",
                sourceSha: "FAKE-SHA",
                targetBranch: "FAKE-TARGET-BRANCH"
            );

            var original = new BuildResultAnalysis
            {
                PipelineName = "fake-pipeline-name",
                BuildId = 12345,
                BuildNumber = "20210101.1",
                TargetBranch = Branch.Parse("main"),
                LinkToBuild = "",
                LinkAllTestResults = "",
                IsRerun = false,
                BuildStatus = BuildStatus.Failed,
                TestResults = new List<TestResult>
                {
                    new TestResult(
                        new TestCaseResult(
                            "TEST-NAME",
                            DateTimeOffset.Parse("2020-01-02T15:04:06.07Z"),
                            TestOutcomeValue.Failed,
                            2345,
                            3456,
                            4567,
                            new PreviousBuildRef(
                                "PREV-BUILD-NUM",
                                DateTimeOffset.Parse("2019-01-02T15:04:06.07Z")
                            ),
                            "FAKE ERROR MESSAGE",
                            "FAKE-STACK-TRACE",
                            "public",
                            "COMMENT",
                            55000
                        ),
                        "https://dev.azure.test/fake/url",
                        new FailureRate
                        {
                            DateOfRate = DateTimeOffset.Parse("2020-08-02T15:04:06.07Z"),
                            FailedRuns = 5,
                            TotalRuns = 10,
                        }
                    )
                },
                BuildStepsResult = new List<StepResult>
                {
                    new StepResult
                    {
                        Errors = new List<Error>
                        {
                            new Error
                            {
                                ErrorMessage = "FAKE BUILD ERROR",
                                LinkLog = "https://dev.azure.test/build-log",
                            }
                        },
                        FailureRate = new FailureRate
                        {
                            DateOfRate = DateTimeOffset.Parse("2020-09-02T15:04:06.07Z"),
                            FailedRuns = 75,
                            TotalRuns = 100,
                        },
                        StepHierarchy = new List<string> {"__default", "STAGE", "JOB", "STEP"},
                        StepName = "TEST-STEP"
                    }
                },
                LatestAttempt = new Attempt
                {
                    AttemptId = 899,
                    LinkBuild = "https://dev.azure.test/attempt-failure-build-link",
                    TestResults = new List<TestResult>
                    {
                        new TestResult(
                            new TestCaseResult(
                                "PREV-TEST-NAME",
                                DateTimeOffset.Parse("2020-12-02T15:04:06.07Z"),
                                TestOutcomeValue.Failed,
                                111,
                                222,
                                333,
                                new PreviousBuildRef(
                                    "PREV-PREV-BUILD-NUM",
                                    DateTimeOffset.Parse("2019-05-05T15:04:06.07Z")
                                ),
                                "FAKE OTHER ERROR MESSAGE",
                                "FAKE-OTHER-STACK-TRACE",
                                "public",
                                "COMMENT",
                                55000
                            ),
                            "https://dev.azure.test/fake/other",
                            new FailureRate
                            {
                                DateOfRate = DateTimeOffset.Parse("2020-06-06T15:04:06.07Z"),
                                FailedRuns = 3,
                                TotalRuns = 9,
                            }
                        )
                    },
                    BuildStepsResult = new List<StepResult>
                    {
                        new StepResult
                        {
                            Errors = new List<Error>
                            {
                                new Error
                                {
                                    ErrorMessage = "FAKE PREV BUILD ERROR",
                                    LinkLog = "https://dev.azure.test/prev-build-log",
                                }
                            },
                            FailureRate = new FailureRate
                            {
                                DateOfRate = DateTimeOffset.Parse("2020-07-07T15:04:06.07Z"),
                                FailedRuns = 4,
                                TotalRuns = 20,
                            },
                            StepHierarchy = new List<string> {"__default", "PREV", "JOB", "STEP"},
                            StepName = "PREV-TEST-STEP"
                        }
                    }
                },
            };

            var sut = TestData.BuildDefault();
            await sut.BuildCacheProvider.PutBuildAsync(buildRef, original, CancellationToken.None);
            var pulled = await sut.BuildCacheProvider.TryGetBuildAsync(buildRef, CancellationToken.None);
            pulled.Should().BeEquivalentTo(original);
        }

        private class MockContextualStorage : BaseContextualStorage
        {
            readonly Dictionary<string, byte[]> _data = new Dictionary<string, byte[]>();
            protected override async Task PutAsync(string root, string name, Stream data, CancellationToken cancellationToken)
            {
                using MemoryStream stream = new MemoryStream();
                await data.CopyToAsync(stream, cancellationToken);
                _data[name] = stream.ToArray();
            }

            protected override Task<Stream> TryGetAsync(string root, string name, CancellationToken cancellationToken)
            {
                if (_data.TryGetValue(name, out byte[] mem))
                {
                    return Task.FromResult<Stream>(new MemoryStream(mem, false));
                }

                return Task.FromResult<Stream>(null);
            }

            public new string PathContext => base.PathContext;
        }
    }
}
