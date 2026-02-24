// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using AwesomeAssertions;
using BuildInsights.AzureStorage.Cache;
using BuildInsights.BuildAnalysis.Models;
using BuildInsights.KnownIssues;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace BuildInsights.BuildAnalysis.Tests
{
    public partial class MergedBuildAnalysisProviderTests
    {
        public sealed class TestData : IDisposable, IAsyncDisposable
        {
            private readonly ServiceProvider _services;

            private TestData(ServiceProvider services)
            {
                _services = services;
            }

            public MergedBuildAnalysisProvider Provider => _services.GetRequiredService<MergedBuildAnalysisProvider>();

            public class Builder
            {
                private readonly ImmutableList<BuildResultAnalysis> _previousAnalyses = [];
                private readonly BuildResultAnalysis _currentAnalysis;
                private readonly ImmutableList<int> _relatedBuildIds = [];
                private readonly ImmutableList<BuildReferenceIdentifier> _filteredBuilds = [];
                private readonly Mock<ICheckResultService> _mockCheckResult = new();
                private readonly Mock<IBuildAnalysisRepositoryConfigurationService> _mockRepoConfiguration = new();

                public Builder()
                {
                }

                public Builder(
                    ImmutableList<BuildResultAnalysis> previousAnalyses,
                    BuildResultAnalysis currentAnalysis,
                    ImmutableList<int> relatedBuildIds,
                    ImmutableList<BuildReferenceIdentifier> filteredBuilds,
                    Mock<ICheckResultService> mockCheckResult,
                    Mock<IBuildAnalysisRepositoryConfigurationService> mockRepoConfiguration)
                {
                    _previousAnalyses = previousAnalyses;
                    _currentAnalysis = currentAnalysis;
                    _relatedBuildIds = relatedBuildIds;
                    _filteredBuilds = filteredBuilds;
                    _mockCheckResult = mockCheckResult;
                    _mockRepoConfiguration = mockRepoConfiguration;
                }


                private Builder With(
                    ImmutableList<BuildResultAnalysis> previousAnalyses = default,
                    BuildResultAnalysis currentAnalysis = default,
                    ImmutableList<int> relatedBuildIds = default,
                    ImmutableList<BuildReferenceIdentifier> filteredBuilds = default,
                    Mock<ICheckResultService> checkResult = default,
                    Mock<IBuildAnalysisRepositoryConfigurationService> repoConfiguration = default)
                {
                    return new Builder(
                        previousAnalyses ?? _previousAnalyses,
                        currentAnalysis ?? _currentAnalysis,
                        relatedBuildIds ?? _relatedBuildIds,
                        filteredBuilds ?? _filteredBuilds,
                        checkResult ?? _mockCheckResult,
                        repoConfiguration ?? _mockRepoConfiguration);
                }


                public Builder WithRelatedBuild(int id)
                {
                    return With(
                        previousAnalyses: _previousAnalyses.Add(new BuildResultAnalysis { BuildId = id, BuildStatus = BuildStatus.Succeeded }),
                        relatedBuildIds: _relatedBuildIds.Add(id)
                    );
                }

                public Builder WithUnrelatedBuild(int id)
                {
                    return With(
                        previousAnalyses: _previousAnalyses.Add(new BuildResultAnalysis { BuildId = id, BuildStatus = BuildStatus.Succeeded })
                    );
                }

                public Builder WithMissingBuild(int id) => With(relatedBuildIds: _relatedBuildIds.Add(id));

                public Builder WithCurrentBuild(int id)
                {
                    return With(currentAnalysis: new BuildResultAnalysis { BuildId = id });
                }

                public Builder WithMockCheckResult(Mock<ICheckResultService> checkResult)
                {
                    return With(checkResult: checkResult);
                }

                public Builder WithRepoConfiguration(Mock<IBuildAnalysisRepositoryConfigurationService> repoConfiguration)
                {
                    return With(repoConfiguration: repoConfiguration);
                }

                public Builder WithFilteredBuilds(ImmutableList<BuildReferenceIdentifier> filteredBuilds)
                {
                    return With(filteredBuilds: filteredBuilds);
                }

                public TestData Build()
                {
                    var collection = new ServiceCollection();

                    var buildService = new Mock<IBuildAnalysisService>();
                    buildService.Setup(
                            b => b.GetBuildResultAnalysisAsync(
                                It.IsAny<BuildReferenceIdentifier>(),
                                It.IsAny<CancellationToken>(),
                                It.IsAny<bool>()
                            )
                        )
                        .ReturnsAsync(_currentAnalysis);

                    var relatedService = new Mock<IRelatedBuildService>();
                    relatedService.Setup(
                            r => r.GetRelatedBuilds(It.IsAny<BuildReferenceIdentifier>(), It.IsAny<CancellationToken>())
                        )
                        .ReturnsAsync(
                            new RelatedBuilds(
                                _relatedBuildIds.Select(
                                    id => new BuildReferenceIdentifier(
                                        OrgId,
                                        ProjectId,
                                        id,
                                        "ANY_URL",
                                        id * 10,
                                        "ANY-DEFINITION-NAME",
                                        "TEST-REPOSIROTY-ID",
                                        CommitHash,
                                        "ANY_TARGET_BRANCH",
                                        true
                                    )
                                )
                            )
                        );

                    var previousBuildService = new Mock<IPreviousBuildAnalysisService>();
                    previousBuildService.Setup(
                            p => p.GetBuildResultAnalysisAsync(
                                It.IsAny<BuildReferenceIdentifier>(),
                                It.IsAny<CancellationToken>(),
                                It.IsAny<bool>()
                            )
                        )
                        .Returns((BuildReferenceIdentifier id, CancellationToken _, bool _) => Task.FromResult(_previousAnalyses.FirstOrDefault(p => p.BuildId == id.BuildId)));

                    var issueService = new Mock<IGitHubIssuesService>();
                    issueService.Setup(i => i.GetCriticalInfrastructureIssuesAsync())
                        .ReturnsAsync([]);

                    var pipelineRequestedProvider = new Mock<IPipelineRequestedService>();
                    pipelineRequestedProvider.Setup(p => p.IsBuildPipelineRequested(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(true);
                    pipelineRequestedProvider.Setup(p => p.GetBuildsByPipelineConfiguration(It.IsAny<ImmutableList<BuildReferenceIdentifier>>(), It.IsAny<NamedBuildReference>()))
                        .ReturnsAsync((ImmutableList<BuildReferenceIdentifier> relatedBuildsParameter, NamedBuildReference namedBuildReference) =>
                            new BuildsByPipelineConfiguration(relatedBuildsParameter, _filteredBuilds));

                    collection.AddSingleton(issueService.Object);
                    collection.AddScoped<MergedBuildAnalysisProvider>();
                    collection.AddScoped<IContextualStorage, MockContextualStorage>();
                    collection.AddSingleton(buildService.Object);
                    collection.AddSingleton(relatedService.Object);
                    collection.AddSingleton(previousBuildService.Object);
                    collection.AddSingleton(_mockCheckResult.Object);
                    collection.AddSingleton(_mockRepoConfiguration.Object);
                    collection.AddSingleton(pipelineRequestedProvider.Object);

                    collection.AddLogging(l => l.AddProvider(new NUnitLogger()));

                    ServiceProvider service = collection.BuildServiceProvider();

                    service.GetRequiredService<IContextualStorage>().SetContext("TEST-CONTEXT");
                    return new TestData(service);
                }
            }

            public static Builder Create() => new();

            public void Dispose()
            {
                _services.Dispose();
            }

            public ValueTask DisposeAsync()
            {
                return _services.DisposeAsync();
            }
        }

        public static readonly DateTimeOffset TestNow = DateTimeOffset.Parse("2020-01-02T15-04-04.07Z");
        public const string CommitHash = "TEST-OPAQUE-COMMIT-HASH";
        public const string OrgId = "TEST-ORG-ID";
        public const string ProjectId = "TEST-PROJECT-ID";
        public const int BuildId = 111222333;

        public static string GetBuildNameFromId(int id) => "test pipeline name " + id;

        public static NamedBuildReference GetRefId(int id) => new(
            GetBuildNameFromId(id),
            $"https://example.test/build/{id}",
            OrgId,
            ProjectId,
            id,
            $"https://example.test/build/{id}",
            id * 10,
            "TEST-DEFINITION-NAME",
            "TEST-REPOSITORY-ID",
            CommitHash,
            "ANY-TARGET-BRANCH",
            true
        );

        [Test]
        public async Task NoAssociatedBuilds()
        {
            await using var test = TestData.Create()
                .WithCurrentBuild(BuildId)
                .Build();

            MergedBuildResultAnalysis analysis = await test.Provider.GetMergedAnalysisAsync(GetRefId(BuildId), MergeBuildAnalysisAction.Include, CancellationToken.None);
            analysis.CommitHash.Should().Be(CommitHash);
            analysis.CompletedPipelines.Should()
                .SatisfyRespectively(
                    a =>
                    {
                        a.BuildId.Should().Be(BuildId);
                    }
                );
        }

        [Test]
        public async Task TwoAssociatedBuilds()
        {
            await using var test = TestData.Create()
                .WithCurrentBuild(BuildId)
                .WithRelatedBuild(222333444)
                .WithRelatedBuild(333444555)
                .Build();

            MergedBuildResultAnalysis analysis = await test.Provider.GetMergedAnalysisAsync(GetRefId(BuildId), MergeBuildAnalysisAction.Include, CancellationToken.None);
            analysis.CommitHash.Should().Be(CommitHash);
            analysis.CompletedPipelines.Should().HaveCount(3);
            analysis.CompletedPipelines.Should().Contain(a => a.BuildId == BuildId);
            analysis.CompletedPipelines.Should().Contain(a => a.BuildId == 222333444);
            analysis.CompletedPipelines.Should().Contain(a => a.BuildId == 333444555);
        }

        [Test]
        public async Task TwoIrrelevantBuilds()
        {
            await using var test = TestData.Create()
                .WithCurrentBuild(BuildId)
                .WithUnrelatedBuild(222333444)
                .WithUnrelatedBuild(333444555)
                .Build();

            MergedBuildResultAnalysis analysis = await test.Provider.GetMergedAnalysisAsync(GetRefId(BuildId), MergeBuildAnalysisAction.Include, CancellationToken.None);
            analysis.CommitHash.Should().Be(CommitHash);
            analysis.CompletedPipelines.Should()
                .SatisfyRespectively(
                    a =>
                    {
                        a.BuildId.Should().Be(BuildId);
                    }
                );
        }

        [Test]
        public async Task OneMissingBuilds()
        {
            await using var test = TestData.Create()
                .WithCurrentBuild(BuildId)
                .WithMissingBuild(999999888)
                .Build();

            MergedBuildResultAnalysis analysis = await test.Provider.GetMergedAnalysisAsync(GetRefId(BuildId), MergeBuildAnalysisAction.Include, CancellationToken.None);
            analysis.CommitHash.Should().Be(CommitHash);
            analysis.CompletedPipelines.Should()
                .SatisfyRespectively(
                    a =>
                    {
                        a.BuildId.Should().Be(BuildId);
                    }
                );
        }

        [Test]
        public async Task AllUpTest()
        {
            await using var test = TestData.Create()
                .WithCurrentBuild(BuildId)
                .WithRelatedBuild(222333444)
                .WithRelatedBuild(333444555)
                .WithMissingBuild(999999888)
                .WithMissingBuild(999999777)
                .WithUnrelatedBuild(555555555)
                .WithUnrelatedBuild(555555666)
                .Build();

            MergedBuildResultAnalysis analysis = await test.Provider.GetMergedAnalysisAsync(GetRefId(BuildId), MergeBuildAnalysisAction.Include, CancellationToken.None);
            analysis.CommitHash.Should().Be(CommitHash);
            analysis.CompletedPipelines.Should().HaveCount(3);
            analysis.CompletedPipelines.Should().Contain(a => a.BuildId == BuildId);
            analysis.CompletedPipelines.Should().Contain(a => a.BuildId == 222333444);
            analysis.CompletedPipelines.Should().Contain(a => a.BuildId == 333444555);
        }

        [Test]
        public async Task ExcludedBuild()
        {
            List<int> pendingBuildNames = [];
            var checkResultService = new Mock<ICheckResultService>();
            checkResultService.Setup(t => t.GetCheckResult(It.IsAny<NamedBuildReference>(),
                It.IsAny<ImmutableList<BuildResultAnalysis>>(), Capture.In(pendingBuildNames), It.IsAny<bool>()));

            await using var test = TestData.Create()
                .WithCurrentBuild(123)
                .WithRelatedBuild(456)
                .WithRelatedBuild(789)
                .WithMockCheckResult(checkResultService)
                .Build();

            // Pretend something told us to exclude build 456 for some reason (presumably because it's been restarted)
            await test.Provider.GetMergedAnalysisAsync(GetRefId(456), MergeBuildAnalysisAction.Exclude, CancellationToken.None);
            MergedBuildResultAnalysis analysis = await test.Provider.GetMergedAnalysisAsync(GetRefId(123), MergeBuildAnalysisAction.Exclude, CancellationToken.None);
            analysis.PendingBuildNames.OrderBy(b => b.Name)
                .Should()
                .SatisfyRespectively(
                    b => b.Name.Should().Be(GetBuildNameFromId(123)),
                    b => b.Name.Should().Be(GetBuildNameFromId(456))
                );
            analysis.CommitHash.Should().Be(CommitHash);
            analysis.CompletedPipelines.Should().HaveCount(1);
            pendingBuildNames[0].Should().Be(2);
        }

        [Test]
        public async Task SingleExcludedBuild()
        {
            List<int> pendingBuildNames = [];
            var checkResultService = new Mock<ICheckResultService>();
            checkResultService.Setup(t => t.GetCheckResult(It.IsAny<NamedBuildReference>(),
                It.IsAny<ImmutableList<BuildResultAnalysis>>(), Capture.In(pendingBuildNames), It.IsAny<bool>()));

            await using var test = TestData.Create()
                .WithCurrentBuild(123)
                .WithMockCheckResult(checkResultService)
                .Build();

            // Pretend something told us to exclude build 456 for some reason (presumably because it's been restarted)
            MergedBuildResultAnalysis analysis = await test.Provider.GetMergedAnalysisAsync(GetRefId(123), MergeBuildAnalysisAction.Exclude, CancellationToken.None);
            analysis.PendingBuildNames.Should().SatisfyRespectively(b => b.Name.Should().Be(GetBuildNameFromId(123)));
            analysis.CommitHash.Should().Be(CommitHash);
            analysis.CompletedPipelines.Should().HaveCount(0);
            pendingBuildNames[0].Should().Be(1);
        }


        [Test]
        public async Task BuildFilteredByPipeline()
        {
            BuildReferenceIdentifier buildReferenceIdentifier = new BuildReferenceIdentifier("", "", 987654321, "", 0, "DefinitionNameTest-A", "", "", "");

            await using var test = TestData.Create()
                .WithCurrentBuild(BuildId)
                .WithFilteredBuilds([buildReferenceIdentifier])
                .Build();

            MergedBuildResultAnalysis analysis = await test.Provider.GetMergedAnalysisAsync(GetRefId(BuildId), MergeBuildAnalysisAction.Include, CancellationToken.None);
            analysis.CommitHash.Should().Be(CommitHash);
            analysis.CompletedPipelines.Should().HaveCount(1);
            analysis.FilteredPipelinesBuilds.Should().HaveCount(1);
            analysis.CompletedPipelines.Should()
                .SatisfyRespectively(
                    a =>
                    {
                        a.BuildId.Should().Be(BuildId);
                    }
                );
            analysis.FilteredPipelinesBuilds.Should()
                .SatisfyRespectively(
                    a =>
                    {
                        a.Name.Should().Be("DefinitionNameTest-A");
                    }
                );
        }
    }
}

internal class MockContextualStorage : BaseContextualStorage
{
    readonly Dictionary<string, byte[]> _data = [];
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
