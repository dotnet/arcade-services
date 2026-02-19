using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.DotNet.Internal.Testing.DependencyInjection.Abstractions;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Models;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Providers;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Services;
using Microsoft.Internal.Helix.GitHub.Services;
using Moq;
using NUnit.Framework;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Tests.Providers;

[TestFixture]
public partial class PipelineRequestedProviderTests
{
    [TestDependencyInjectionSetup]
    public static class TestSetup
    {
        public static void Defaults(IServiceCollection services)
        {
            services.AddLogging(l => l.AddProvider(new NUnitLogger()));
            services.Configure<BuildAnalysisFileSettings>(o =>
            {
                o.Path = "TestFilePath/";
                o.FileName = "TestingFileName";
            });
        }

        public static Func<IServiceProvider, Mock<IGithubRepositoryService>> GithubRepositoryProvider(IServiceCollection collection, string buildAnalysisFile)
        {
            var githubRepositoryServiceMock = new Mock<IGithubRepositoryService>();
            githubRepositoryServiceMock.Setup(m => m.GetFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(buildAnalysisFile);

            collection.AddSingleton(githubRepositoryServiceMock.Object);
            return _ => githubRepositoryServiceMock;
        }

        public static Func<IServiceProvider, PipelineRequestedProvider> Provider(IServiceCollection collection)
        {
            collection.AddSingleton<PipelineRequestedProvider>();
            return s => s.GetRequiredService<PipelineRequestedProvider>();
        }
    }


    [Test]
    public async Task IsBuildPipelineRequestedNoBuildAnalysisSettingsFileTest()
    {
        await using TestData testData = await TestData.Default.BuildAsync();
        bool result = await testData.Provider.IsBuildPipelineRequested("", "", 1, 0);
        result.Should().BeTrue();
    }

    [Test]
    public async Task IsBuildPipelineRequestedNoBuildAnalysisFileEmptyTest()
    {
        await using TestData testData = await TestData.Default.WithBuildAnalysisFile(string.Empty).BuildAsync();
        bool result = await testData.Provider.IsBuildPipelineRequested("", "", 1, 0);
        result.Should().BeTrue();
    }

    [Test]
    public async Task IsBuildPipelineRequestedWhenPipelineIsNotAnalyze()
    {
        string buildAnalysisSettingsFile = GetBuildAnalysisSettingsFile(new List<int> {2});
        await using TestData testData = await TestData.Default.WithBuildAnalysisFile(buildAnalysisSettingsFile).BuildAsync();
        bool result = await testData.Provider.IsBuildPipelineRequested("", "", 1, 0);
        result.Should().BeFalse();
    }

    [Test]
    public async Task IsBuildPipelineRequestedWhenPipelineIsRequested()
    {
        string buildAnalysisSettingsFile = GetBuildAnalysisSettingsFile(new List<int> {1});
        await using TestData testData = await TestData.Default.WithBuildAnalysisFile(buildAnalysisSettingsFile).BuildAsync();
        bool result = await testData.Provider.IsBuildPipelineRequested("", "", 1, 0);
        result.Should().BeTrue();
    }

    [Test]
    public async Task GetBuildsByPipelineWhenNoBuildAnalysisSettingsFiles()
    {
        await using TestData testData = await TestData.Default.WithBuildAnalysisFile(string.Empty).BuildAsync();
        var relatedBuilds = new List<BuildReferenceIdentifier> {MockBuildReference(1), MockBuildReference(2), MockBuildReference(3)};
        NamedBuildReference mainBuild = MockBuildReference(1);

        BuildsByPipelineConfiguration result = await testData.Provider.GetBuildsByPipelineConfiguration(relatedBuilds.ToImmutableList(), mainBuild);
        result.FilteredPipelinesBuilds.Should().HaveCount(0);
        result.IncludedPipelinesBuilds.Should().HaveCount(3);
    }

    [Test]
    public async Task GetBuildsByPipelineWhenAllPipelinesFiltered()
    {
        string buildAnalysisSettingsFile = GetBuildAnalysisSettingsFile(new List<int> {6});

        await using TestData testData = await TestData.Default.WithBuildAnalysisFile(buildAnalysisSettingsFile).BuildAsync();
        var relatedBuilds = new List<BuildReferenceIdentifier> {MockBuildReference(1), MockBuildReference(2), MockBuildReference(3)};
        NamedBuildReference mainBuild = MockBuildReference(1);

        BuildsByPipelineConfiguration result = await testData.Provider.GetBuildsByPipelineConfiguration(relatedBuilds.ToImmutableList(), mainBuild);
        result.FilteredPipelinesBuilds.Should().HaveCount(3);
        result.IncludedPipelinesBuilds.Should().HaveCount(0);
    }

    [Test]
    public async Task GetBuildsByPipelineSomeFilteredOtherIncluded()
    {
        string buildAnalysisSettingsFile = GetBuildAnalysisSettingsFile(new List<int> {1, 3});

        await using TestData testData = await TestData.Default.WithBuildAnalysisFile(buildAnalysisSettingsFile).BuildAsync();
        var relatedBuilds = new List<BuildReferenceIdentifier> {MockBuildReference(123, 1), MockBuildReference(345, 2), MockBuildReference(456, 3)};
        NamedBuildReference mainBuild = MockBuildReference(123, 1);

        BuildsByPipelineConfiguration result = await testData.Provider.GetBuildsByPipelineConfiguration(relatedBuilds.ToImmutableList(), mainBuild);
        result.FilteredPipelinesBuilds.Should().HaveCount(1);
        result.IncludedPipelinesBuilds.Should().HaveCount(2);

        result.FilteredPipelinesBuilds.First().BuildId.Should().Be(345);
    }


    private NamedBuildReference MockBuildReference(int buildId, int definitionId)
    {
        return new NamedBuildReference("", "", "", "", buildId, "", definitionId, "", "", "", "");
    }

    private NamedBuildReference MockBuildReference(int definitionId)
    {
        return new NamedBuildReference("", "", "", "", 12345, "", definitionId, "", "", "", "");
    }

    private string GetBuildAnalysisSettingsFile(List<int> pipelineIdsToAnalyze)
    {
        var buildAnalysisRepositorySettings = new BuildAnalysisRepositorySettings
        {
            PipelinesToAnalyze = pipelineIdsToAnalyze.Select(pipelineId => new PipelineData {PipelineId = pipelineId})
                .ToList()
        };

        return JsonSerializer.Serialize(buildAnalysisRepositorySettings);
    }
}
