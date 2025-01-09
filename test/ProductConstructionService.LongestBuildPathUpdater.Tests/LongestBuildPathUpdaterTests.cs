// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data;
using Maestro.Data.Models;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.Kusto;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;

namespace ProductConstructionService.LongestBuildPathUpdater.Tests;

[TestFixture]
public class LongestBuildPathUpdaterTests
{
    private BuildAssetRegistryContext? _context;
    private ServiceProvider? _provider;
    private IServiceScope _scope = new Mock<IServiceScope>().Object;
    private Mock<IBasicBarClient> _barMock = new();

    [SetUp]
    public void Setup()
    {
        var services = new ServiceCollection();

        _barMock = new Mock<IBasicBarClient>();
        services.AddLogging();
        services.AddDbContext<BuildAssetRegistryContext>(
            options =>
            {
                options.UseInMemoryDatabase("BuildAssetRegistry");
                options.EnableServiceProviderCaching(false);
            });
        services.AddSingleton(new Mock<IRemoteFactory>().Object);
        services.AddSingleton(_barMock.Object);
        services.AddSingleton(new Mock<IHostEnvironment>().Object);
        services.AddSingleton(_ => new Mock<IKustoClientProvider>().Object);

        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();

        _context = _scope.ServiceProvider.GetRequiredService<BuildAssetRegistryContext>();
    }

    [TearDown]
    public void TearDown()
    {
        _scope.Dispose();
        _provider!.Dispose();
    }

    [Test]
    public async Task ShouldSaveCorrectBestAndWorstPathTimes()
    {
        var graph1 = CreateGraph(
            (Repo: "a", BestCaseTime: 1, WorstCaseTime: 7, OnLongestBuildPath: true),
            (Repo: "b", BestCaseTime: 2, WorstCaseTime: 5, OnLongestBuildPath: true),
            (Repo: "c", BestCaseTime: 9, WorstCaseTime: 9, OnLongestBuildPath: false));

        var graph2 = CreateGraph(
            (Repo: "g", BestCaseTime: 10, WorstCaseTime: 70, OnLongestBuildPath: true),
            (Repo: "h", BestCaseTime: 20, WorstCaseTime: 50, OnLongestBuildPath: true));

        SetupBar(
            (ChannelId: 1, Graph: graph1),
            (ChannelId: 2, Graph: graph2));

        var updater = ActivatorUtilities.CreateInstance<LongestBuildPathUpdater>(_scope.ServiceProvider);
        await updater.UpdateLongestBuildPathAsync();

        var longestBuildPaths = _context!.LongestBuildPaths.ToList();
        longestBuildPaths.Should().HaveCount(2);

        var firstChannelData = longestBuildPaths.FirstOrDefault(x => x.ChannelId == 1);
        firstChannelData.Should().BeEquivalentTo(new LongestBuildPath
        {
            BestCaseTimeInMinutes = 2,
            ChannelId = 1,
            WorstCaseTimeInMinutes = 7,
            ContributingRepositories = "b@main;a@main"
        }, options => options
            .Excluding(x => x.Channel)
            .Excluding(x => x.Id)
            .Excluding(x => x.ReportDate));

        var secondChannelData = longestBuildPaths.FirstOrDefault(x => x.ChannelId == 2);
        secondChannelData.Should().BeEquivalentTo(new LongestBuildPath
        {
            BestCaseTimeInMinutes = 20,
            ChannelId = 2,
            WorstCaseTimeInMinutes = 70,
            ContributingRepositories = "h@main;g@main"
        }, options => options
            .Excluding(x => x.Channel)
            .Excluding(x => x.Id)
            .Excluding(x => x.ReportDate));
    }

    [Test]
    public async Task ShouldNotAddLongestBuildPathRowWhenThereAreNoNodesOnLongestBuildPath()
    {
        var graph = CreateGraph(
            (Repo: "a", BestCaseTime: 1, WorstCaseTime: 7, OnLongestBuildPath: false),
            (Repo: "b", BestCaseTime: 2, WorstCaseTime: 5, OnLongestBuildPath: false));

        SetupBar((ChannelId: 1, Graph: graph));

        var updater = ActivatorUtilities.CreateInstance<ProductConstructionService.LongestBuildPathUpdater.LongestBuildPathUpdater>(_scope.ServiceProvider);
        await updater.UpdateLongestBuildPathAsync();

        var longestBuildPaths = _context!.LongestBuildPaths.ToList();
        longestBuildPaths.Should().BeEmpty();
    }

    [Test]
    public async Task ShouldNotAddLongestBuildPathRowWhenGraphIsEmpty()
    {
        var graph = new DependencyFlowGraph([], []);

        SetupBar((ChannelId: 1, Graph: graph));

        var updater = ActivatorUtilities.CreateInstance<ProductConstructionService.LongestBuildPathUpdater.LongestBuildPathUpdater>(_scope.ServiceProvider);
        await updater.UpdateLongestBuildPathAsync();

        var longestBuildPaths = _context!.LongestBuildPaths.ToList();
        longestBuildPaths.Should().BeEmpty();
    }

    private static DependencyFlowGraph CreateGraph(
        params (string Repo, double BestCaseTime, double WorstCaseTime, bool OnLongestBuildPath)[] nodes)
    {
        var graphNodes = nodes
            .Select((n, i) => new DependencyFlowNode(n.Repo, "main", $"Node{i}")
            {
                BestCasePathTime = n.BestCaseTime,
                OnLongestBuildPath = n.OnLongestBuildPath,
                WorstCasePathTime = n.WorstCaseTime
            })
            .ToList();

        return new DependencyFlowGraph(graphNodes, []);
    }

    private void SetupBar(
        params (int ChannelId, DependencyFlowGraph Graph)[] graphPerChannel)
    {
        foreach (var item in graphPerChannel)
        {
            _barMock
                .Setup(m => m.GetDependencyFlowGraphAsync(
                    item.ChannelId,
                    It.IsAny<int>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<IReadOnlyList<string>>()))
                .ReturnsAsync(item.Graph);

            _context!.Channels.Add(new Channel
            {
                Id = item.ChannelId,
                Name = $"Channel_{item.ChannelId}",
                Classification = "Pizza",
            });
        }

        _context!.SaveChanges();
    }
}
