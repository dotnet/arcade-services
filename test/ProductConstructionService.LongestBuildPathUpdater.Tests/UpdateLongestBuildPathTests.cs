// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data;
using Maestro.Data.Models;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Kusto;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;

namespace ProductConstructionService.LongestBuildPathUpdater.Tests;

[TestFixture]
public class UpdateLongestBuildPathTests
{
    private BuildAssetRegistryContext _context;
    private Mock<IHostEnvironment> _env;
    private ServiceProvider _provider;
    private IServiceScope _scope;
    private Mock<IRemoteFactory> _remoteFactory;
    private Mock<IBasicBarClient> _barMock;

    [SetUp]
    public void Setup()
    {
        var services = new ServiceCollection();
        _remoteFactory = new(MockBehavior.Strict);
        _barMock = new(MockBehavior.Strict);
        _env = new(MockBehavior.Strict);

        services.AddSingleton(_env.Object);
        services.AddLogging();
        services.AddDbContext<BuildAssetRegistryContext>(
            options =>
            {
                options.UseInMemoryDatabase("BuildAssetRegistry");
                options.EnableServiceProviderCaching(false);
            });
        services.AddSingleton(_remoteFactory.Object);
        services.AddSingleton(_barMock.Object);
        services.AddSingleton<IKustoClientProvider>(_ => null);

        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();

        _context = _scope.ServiceProvider.GetRequiredService<BuildAssetRegistryContext>();
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

            _context.Channels.Add(new Channel
            {
                Id = item.ChannelId,
                Name = $"Channel_{item.ChannelId}",
                Classification = "Pizza",
            });
        }

        _context.SaveChanges();
    }
}
