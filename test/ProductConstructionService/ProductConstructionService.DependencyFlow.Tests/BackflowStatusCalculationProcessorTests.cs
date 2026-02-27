// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using Maestro.Data;
using Maestro.Data.Models;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using ProductConstructionService.DependencyFlow.Models;
using ProductConstructionService.DependencyFlow.Tests.Mocks;
using ProductConstructionService.DependencyFlow.WorkItemProcessors;
using ProductConstructionService.DependencyFlow.WorkItems;

namespace ProductConstructionService.DependencyFlow.Tests;

[TestFixture]
public class BackflowStatusCalculationProcessorTests
{
    private const string VmrUri = "https://github.com/dotnet/dotnet";
    private const string VmrCommit = "abc123def456";
    private const string TargetRepo1 = "https://github.com/dotnet/runtime";
    private const string TargetRepo2 = "https://github.com/dotnet/aspnetcore";
    private const string TargetBranch = "main";
    private const string LastBackflowedSha1 = "sha111111";
    private const string LastBackflowedSha2 = "sha222222";

    private BuildAssetRegistryContext _context = null!;
    private Mock<IRemoteFactory> _remoteFactory = null!;
    private MockRedisCacheFactory _redisCacheFactory = null!;
    private Mock<IVmrCloneManager> _vmrCloneManager = null!;
    private Mock<ILocalGitRepo> _vmrClone = null!;
    private Mock<ILogger<BackflowStatusCalculationProcessor>> _logger = null!;
    private Mock<IRemote> _remote1 = null!;
    private Mock<IRemote> _remote2 = null!;

    private Channel _channel = null!;
    private DefaultChannel _defaultChannel = null!;
    private Subscription _subscription1 = null!;
    private Subscription _subscription2 = null!;
    private Build _vmrBuild = null!;

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<BuildAssetRegistryContext>()
            .UseInMemoryDatabase(databaseName: "BuildAssetRegistry_" + Guid.NewGuid())
            .Options;

        _context = new BuildAssetRegistryContext(options);

        _remoteFactory = new Mock<IRemoteFactory>();
        _redisCacheFactory = new MockRedisCacheFactory();
        _vmrCloneManager = new Mock<IVmrCloneManager>();
        _vmrClone = new Mock<ILocalGitRepo>();
        _logger = new Mock<ILogger<BackflowStatusCalculationProcessor>>();
        _remote1 = new Mock<IRemote>();
        _remote2 = new Mock<IRemote>();

        SetupTestData();
    }

    [TearDown]
    public void TearDown()
    {
        _context.Dispose();
    }

    private void SetupTestData()
    {
        _channel = new Channel
        {
            Id = 1,
            Name = "test-channel",
            Classification = "test"
        };

        _defaultChannel = new DefaultChannel
        {
            Id = 1,
            Repository = VmrUri,
            Branch = "main",
            Channel = _channel,
            ChannelId = _channel.Id,
            Enabled = true
        };

        _subscription1 = new Subscription
        {
            Id = Guid.NewGuid(),
            Channel = _channel,
            ChannelId = _channel.Id,
            SourceRepository = VmrUri,
            TargetRepository = TargetRepo1,
            TargetBranch = TargetBranch,
            SourceEnabled = true,
            PolicyObject = new SubscriptionPolicy
            {
                UpdateFrequency = UpdateFrequency.EveryBuild,
                Batchable = false
            }
        };

        _subscription2 = new Subscription
        {
            Id = Guid.NewGuid(),
            Channel = _channel,
            ChannelId = _channel.Id,
            SourceRepository = VmrUri,
            TargetRepository = TargetRepo2,
            TargetBranch = TargetBranch,
            SourceEnabled = true,
            PolicyObject = new SubscriptionPolicy
            {
                UpdateFrequency = UpdateFrequency.EveryBuild,
                Batchable = false
            }
        };

        _vmrBuild = new Build
        {
            Id = 100,
            Commit = VmrCommit,
            GitHubRepository = VmrUri,
            GitHubBranch = "main",
            DateProduced = DateTimeOffset.UtcNow.AddHours(-1),
            Assets = []
        };
    }

    private async Task SeedDatabaseAsync()
    {
        _context.Channels.Add(_channel);
        _context.DefaultChannels.Add(_defaultChannel);
        _context.Subscriptions.Add(_subscription1);
        _context.Subscriptions.Add(_subscription2);
        _context.Builds.Add(_vmrBuild);
        await _context.SaveChangesAsync();
    }

    private BackflowStatusCalculationProcessor CreateProcessor()
    {
        return new BackflowStatusCalculationProcessor(
            _context,
            _remoteFactory.Object,
            _redisCacheFactory,
            _vmrCloneManager.Object,
            _logger.Object);
    }

    [Test]
    public async Task ProcessWorkItemAsync_WithTwoBackflowSubscriptions_ReturnsStatusForBoth()
    {
        // Arrange
        await SeedDatabaseAsync();

        var workItem = new BackflowStatusCalculationWorkItem { VmrBuildId = _vmrBuild.Id };

        SetupVmrCloneManager();
        SetupRemoteFactory();
        SetupSourceTag(LastBackflowedSha1, LastBackflowedSha2);
        SetupGitRevListCommand(LastBackflowedSha1, commitDistance: 5);
        SetupGitRevListCommand(LastBackflowedSha2, commitDistance: 10);

        var processor = CreateProcessor();

        // Act
        var result = await processor.ProcessWorkItemAsync(workItem, CancellationToken.None);

        // Assert
        result.Should().BeTrue();

        var cachedStatus = await GetCachedStatusAsync();
        cachedStatus.Should().NotBeNull();
        cachedStatus.VmrCommitSha.Should().Be(VmrCommit);
        cachedStatus.BranchStatuses.Should().ContainKey("main");

        var branchStatus = cachedStatus.BranchStatuses["main"];
        branchStatus.SubscriptionStatuses.Should().HaveCount(2);

        var status1 = branchStatus.SubscriptionStatuses.First(s => s.TargetRepository == TargetRepo1);
        status1.LastBackflowedSha.Should().Be(LastBackflowedSha1);
        status1.CommitDistance.Should().Be(5);
        status1.SubscriptionId.Should().Be(_subscription1.Id);

        var status2 = branchStatus.SubscriptionStatuses.First(s => s.TargetRepository == TargetRepo2);
        status2.LastBackflowedSha.Should().Be(LastBackflowedSha2);
        status2.CommitDistance.Should().Be(10);
        status2.SubscriptionId.Should().Be(_subscription2.Id);
    }

    [Test]
    public async Task ProcessWorkItemAsync_WithInternalBranch_ProcessesBothBranches()
    {
        // Arrange
        _vmrBuild.GitHubBranch = "internal/release/8.0";
        _defaultChannel.Branch = "internal/release/8.0";

        var publicDefaultChannel = new DefaultChannel
        {
            Id = 2,
            Repository = VmrUri,
            Branch = "release/8.0",
            Channel = _channel,
            ChannelId = _channel.Id,
            Enabled = true
        };

        await SeedDatabaseAsync();
        _context.DefaultChannels.Add(publicDefaultChannel);
        await _context.SaveChangesAsync();

        var workItem = new BackflowStatusCalculationWorkItem { VmrBuildId = _vmrBuild.Id };

        SetupVmrCloneManager(["internal/release/8.0", "release/8.0"]);
        SetupRemoteFactory();
        SetupSourceTag(LastBackflowedSha1, LastBackflowedSha2);
        SetupGitRevListCommand(LastBackflowedSha1, commitDistance: 3);
        SetupGitRevListCommand(LastBackflowedSha2, commitDistance: 7);

        var processor = CreateProcessor();

        // Act
        var result = await processor.ProcessWorkItemAsync(workItem, CancellationToken.None);

        // Assert
        result.Should().BeTrue();

        var cachedStatus = await GetCachedStatusAsync();
        cachedStatus.Should().NotBeNull();
        cachedStatus.BranchStatuses.Should().ContainKey("internal/release/8.0");
        cachedStatus.BranchStatuses.Should().ContainKey("release/8.0");
    }

    [Test]
    public async Task ProcessWorkItemAsync_WhenBuildNotFound_ReturnsFalse()
    {
        // Arrange
        await SeedDatabaseAsync();

        var workItem = new BackflowStatusCalculationWorkItem { VmrBuildId = 999 };
        var processor = CreateProcessor();

        // Act
        var result = await processor.ProcessWorkItemAsync(workItem, CancellationToken.None);

        // Assert
        result.Should().BeFalse();
    }

    [Test]
    public async Task ProcessWorkItemAsync_WhenNoDefaultChannel_SkipsBranchGracefully()
    {
        // Arrange
        _context.Channels.Add(_channel);
        _context.Builds.Add(_vmrBuild);
        await _context.SaveChangesAsync();

        var workItem = new BackflowStatusCalculationWorkItem { VmrBuildId = _vmrBuild.Id };

        SetupVmrCloneManager();

        var processor = CreateProcessor();

        // Act
        var result = await processor.ProcessWorkItemAsync(workItem, CancellationToken.None);

        // Assert
        result.Should().BeTrue();

        var cachedStatus = await GetCachedStatusAsync();
        cachedStatus.Should().NotBeNull();
        cachedStatus.BranchStatuses.Should().BeEmpty();
    }

    [Test]
    public async Task ProcessWorkItemAsync_WhenVersionDetailsHasNoSource_SkipsSubscription()
    {
        // Arrange
        await SeedDatabaseAsync();

        var workItem = new BackflowStatusCalculationWorkItem { VmrBuildId = _vmrBuild.Id };

        SetupVmrCloneManager();
        SetupRemoteFactory();

        // Return null Source for first subscription, valid for second
        SetupSourceTag(null!, LastBackflowedSha2);
        SetupGitRevListCommand(LastBackflowedSha2, commitDistance: 8);

        var processor = CreateProcessor();

        // Act
        var result = await processor.ProcessWorkItemAsync(workItem, CancellationToken.None);

        // Assert
        result.Should().BeTrue();

        var cachedStatus = await GetCachedStatusAsync();
        cachedStatus.Should().NotBeNull();

        var branchStatus = cachedStatus.BranchStatuses["main"];
        branchStatus.SubscriptionStatuses.Should().HaveCount(1);
        branchStatus.SubscriptionStatuses[0].TargetRepository.Should().Be(TargetRepo2);
    }

    [Test]
    public async Task ProcessWorkItemAsync_WhenGitRevListFails_SetsCommitDistanceToZero()
    {
        // Arrange
        await SeedDatabaseAsync();

        var workItem = new BackflowStatusCalculationWorkItem { VmrBuildId = _vmrBuild.Id };

        SetupVmrCloneManager();
        SetupRemoteFactory();
        SetupSourceTag(LastBackflowedSha1, LastBackflowedSha2);

        // Setup git rev-list to fail
        _vmrClone
            .Setup(c => c.ExecuteGitCommand(
                It.Is<string[]>(args => args.Contains("rev-list")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessExecutionResult
            {
                ExitCode = 1,
                StandardError = "fatal: bad revision"
            });

        var processor = CreateProcessor();

        // Act
        var result = await processor.ProcessWorkItemAsync(workItem, CancellationToken.None);

        // Assert
        result.Should().BeTrue();

        var cachedStatus = await GetCachedStatusAsync();
        cachedStatus.Should().NotBeNull();

        var branchStatus = cachedStatus.BranchStatuses["main"];
        branchStatus.SubscriptionStatuses.Should().AllSatisfy(s => s.CommitDistance.Should().Be(0));
    }

    private void SetupVmrCloneManager(string[]? expectedBranches = null)
    {
        expectedBranches ??= ["main"];

        _vmrCloneManager
            .Setup(m => m.PrepareVmrAsync(
                It.Is<IReadOnlyCollection<string>>(uris => uris.Contains(VmrUri)),
                It.Is<IReadOnlyCollection<string>>(branches => expectedBranches.All(b => branches.Contains(b))),
                It.IsAny<string>(),
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(_vmrClone.Object);
    }

    private void SetupRemoteFactory()
    {
        _remoteFactory
            .Setup(f => f.CreateRemoteAsync(TargetRepo1))
            .ReturnsAsync(_remote1.Object);

        _remoteFactory
            .Setup(f => f.CreateRemoteAsync(TargetRepo2))
            .ReturnsAsync(_remote2.Object);
    }

    private void SetupSourceTag(string sha1, string sha2)
    {
        _remote1
            .Setup(p => p.GetSourceDependencyAsync(TargetRepo1, TargetBranch))
            .ReturnsAsync(new SourceDependency(VmrUri, "runtime", sha1, -1));

        _remote2
            .Setup(p => p.GetSourceDependencyAsync(TargetRepo2, TargetBranch))
            .ReturnsAsync(new SourceDependency(VmrUri, "runtime", sha2, -1));
    }

    private void SetupGitRevListCommand(string fromSha, int commitDistance)
    {
        _vmrClone
            .Setup(c => c.ExecuteGitCommand(
                It.Is<string[]>(args =>
                    args.Length == 3 &&
                    args[0] == "rev-list" &&
                    args[1] == "--count" &&
                    args[2] == $"{fromSha}..{VmrCommit}"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessExecutionResult
            {
                ExitCode = 0,
                StandardOutput = commitDistance.ToString()
            });
    }

    private Task<BackflowStatus?> GetCachedStatusAsync()
    {
        var cache = _redisCacheFactory.Create<BackflowStatus>(VmrCommit, includeTypeInKey: true);
        return cache.TryGetStateAsync();
    }
}
