// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using ProductConstructionService.Common;
using Maestro.Data;
using Maestro.Data.Models;
using Maestro.DataProviders.ConfigurationIngestion;
using Microsoft.DotNet.MaestroConfiguration.Client.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProductConstructionService.Api.Tests;
using Moq;

namespace Maestro.DataProviders.Tests;

[TestFixture]
public class ConfigurationIngestorTests
{
    private TestDatabase _testDatabase = null!;
    private BuildAssetRegistryContext _context = null!;
    private IConfigurationIngestor _ingestor = null!;
    private string _testNamespace = string.Empty;

    [SetUp]
    public async Task SetUp()
    {
        _testNamespace = "test-namespace-" + Guid.NewGuid();

        // TODO: This still creates a whole DB per test which is super slow
        //       We should only create the DB per test suite via a OneTimeSetUp and OneTimeTearDown
        _testDatabase = new TestDatabase("TestDB_Ingestion_" + TestContext.CurrentContext.Test.MethodName);
        var connectionString = await _testDatabase.GetConnectionString();

        var options = new DbContextOptionsBuilder<BuildAssetRegistryContext>()
            .UseSqlServer(connectionString)
            .Options;

        _context = new BuildAssetRegistryContext(options);

        var distributedLockMock = new Mock<IDistributedLock>();

        distributedLockMock
            .Setup(dl => dl.ExecuteWithLockAsync<object>(
                It.IsAny<string>(),
                It.IsAny<Func<Task<object>>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, Func<Task<object>>, TimeSpan?, CancellationToken>(async (key, func, timeout, token) => await func());

        var services = new ServiceCollection()
            .AddSingleton(_context)
            .AddSingleton<ISqlBarClient>(new SqlBarClient(_context, null))
            .AddSingleton(distributedLockMock.Object)
            .AddConfigurationIngestion();

        _ingestor = services.BuildServiceProvider()
            .GetRequiredService<IConfigurationIngestor>();

        // Pre-populate repositories used in tests
        await EnsureRepositoriesExist(
            "https://github.com/dotnet/runtime",
            "https://github.com/dotnet/runtime2",
            "https://github.com/dotnet/runtime3",
            "https://github.com/dotnet/aspnetcore",
            "https://github.com/dotnet/old",
            "https://github.com/dotnet/target");
    }

    [TearDown]
    public void TearDown()
    {
        _context?.Dispose();
        _testDatabase?.Dispose();
    }

    #region Empty Database Tests

    [Test]
    public async Task IngestConfigurationAsync_EmptyDatabase_CreatesAllEntities()
    {
        // Arrange
        var configData = CreateBasicConfigurationData();

        // Act
        var result = await _ingestor.IngestConfigurationAsync(configData, _testNamespace);

        // Assert
        result.Channels.Creations.Should().HaveCount(1);
        result.Channels.Updates.Should().BeEmpty();
        result.Channels.Removals.Should().BeEmpty();

        result.Subscriptions.Creations.Should().HaveCount(1);
        result.Subscriptions.Updates.Should().BeEmpty();
        result.Subscriptions.Removals.Should().BeEmpty();

        result.DefaultChannels.Creations.Should().HaveCount(1);
        result.DefaultChannels.Updates.Should().BeEmpty();
        result.DefaultChannels.Removals.Should().BeEmpty();

        result.RepositoryBranches.Creations.Should().HaveCount(1);
        result.RepositoryBranches.Updates.Should().BeEmpty();
        result.RepositoryBranches.Removals.Should().BeEmpty();

        var namespaceEntity = await _context.Namespaces
            .Include(n => n.Channels)
            .FirstOrDefaultAsync(n => n.Name == _testNamespace);

        namespaceEntity.Should().NotBeNull();
        namespaceEntity.Channels.Should().ContainSingle()
            .Which.Name.Should().Be(".NET 8");
    }

    [Test]
    public async Task IngestConfigurationAsync_EmptyDatabase_CreatesNamespace()
    {
        // Arrange
        var configData = CreateBasicConfigurationData();

        // Act
        await _ingestor.IngestConfigurationAsync(configData, _testNamespace);

        // Assert
        var namespaceEntity = await _context.Namespaces
            .FirstOrDefaultAsync(ns => ns.Name == _testNamespace);

        namespaceEntity.Should().NotBeNull();
        namespaceEntity.Name.Should().Be(_testNamespace);
    }

    #endregion

    #region Update Tests

    [Test]
    public async Task IngestConfigurationAsync_UpdateChannel_UpdatesClassification()
    {
        // Arrange
        var namespaceEntity = await CreateNamespace();
        var existingChannel = CreateChannel("Test Channel", "dev", namespaceEntity);
        await _context.Channels.AddAsync(existingChannel);
        await _context.SaveChangesAsync();

        var updatedChannelYaml = new ChannelYaml
        {
            Name = "Test Channel",
            Classification = "production",
        };

        var configData = new ConfigurationData(
            [],
            [updatedChannelYaml],
            [],
            []);

        // Act
        var result = await _ingestor.IngestConfigurationAsync(configData, _testNamespace);

        // Assert
        result.Channels.Updates.Should().HaveCount(1);
        result.Channels.Creations.Should().BeEmpty();
        result.Channels.Removals.Should().BeEmpty();

        namespaceEntity = await _context.Namespaces
            .Include(n => n.Channels)
            .FirstOrDefaultAsync(n => n.Name == _testNamespace);

        namespaceEntity.Should().NotBeNull();
        var updatedChannel = namespaceEntity.Channels.FirstOrDefault(c => c.Name == "Test Channel");
        updatedChannel.Should().NotBeNull();
        updatedChannel.Classification.Should().Be("production");
    }

    [Test]
    public async Task IngestConfigurationAsync_UpdateSubscription_UpdatesProperties()
    {
        // Arrange
        var namespaceEntity = await CreateNamespace();
        var channel = CreateChannel(".NET 8", "release", namespaceEntity);
        await _context.Channels.AddAsync(channel);
        await _context.SaveChangesAsync();

        var subscriptionId = Guid.NewGuid();
        var existingSubscription = CreateSubscription(
            subscriptionId,
            channel,
            "https://github.com/dotnet/runtime",
            "https://github.com/dotnet/aspnetcore",
            "main",
            enabled: true,
            namespaceEntity);

        await _context.Subscriptions.AddAsync(existingSubscription);
        await _context.SaveChangesAsync();

        var updatedSubscription = new SubscriptionYaml
        {
            Id = subscriptionId,
            Channel = ".NET 8",
            SourceRepository = "https://github.com/dotnet/runtime",
            TargetRepository = "https://github.com/dotnet/aspnetcore",
            TargetBranch = "main",
            Enabled = false, // Changed from true to false
            UpdateFrequency = Microsoft.DotNet.ProductConstructionService.Client.Models.UpdateFrequency.EveryBuild,
        };

        var configData = new ConfigurationData(
            [updatedSubscription],
            [new ChannelYaml { Name = ".NET 8", Classification = "release" }],
            [],
            []);

        // Act
        var result = await _ingestor.IngestConfigurationAsync(configData, _testNamespace);

        result.Subscriptions.Updates.Should().HaveCount(1);
    }

    [Test]
    public async Task IngestConfigurationAsync_UpdateSubscriptionExcludedAssets_AddsNewAssetFilters()
    {
        // Arrange
        var namespaceEntity = await CreateNamespace();
        var channel = CreateChannel(".NET 8", "release", namespaceEntity);
        await _context.Channels.AddAsync(channel);
        await _context.SaveChangesAsync();

        var subscriptionId = Guid.NewGuid();
        var existingSubscription = CreateSubscription(
            subscriptionId,
            channel,
            "https://github.com/dotnet/runtime",
            "https://github.com/dotnet/aspnetcore",
            "main",
            enabled: true,
            namespaceEntity,
            excludedAssets:
            [
                new AssetFilter { Filter = "Microsoft.NET.Sdk" },
            ]);

        await _context.Subscriptions.AddAsync(existingSubscription);
        await _context.SaveChangesAsync();

        var updatedSubscription = new SubscriptionYaml
        {
            Id = subscriptionId,
            Channel = ".NET 8",
            SourceRepository = "https://github.com/dotnet/runtime",
            TargetRepository = "https://github.com/dotnet/aspnetcore",
            TargetBranch = "main",
            Enabled = true,
            ExcludedAssets =
            [
                "Microsoft.NET.Sdk",
                "Microsoft.AspNetCore.*",
                "System.Text.Json",
            ],
        };

        var configData = new ConfigurationData(
            [updatedSubscription],
            [new ChannelYaml { Name = ".NET 8", Classification = "release" }],
            [],
            []);

        // Act
        var result = await _ingestor.IngestConfigurationAsync(configData, _testNamespace);

        // Assert
        result.Subscriptions.Updates.Should().HaveCount(1);

        var updated = await _context.Subscriptions
            .Include(s => s.ExcludedAssets)
            .FirstOrDefaultAsync(s => s.Id == subscriptionId);

        updated.Should().NotBeNull();
        updated.ExcludedAssets.Should().HaveCount(3);
        updated.ExcludedAssets.Should().Contain(a => a.Filter == "Microsoft.NET.Sdk");
        updated.ExcludedAssets.Should().Contain(a => a.Filter == "Microsoft.AspNetCore.*");
        updated.ExcludedAssets.Should().Contain(a => a.Filter == "System.Text.Json");
    }

    [Test]
    public async Task IngestConfigurationAsync_UpdateSubscriptionExcludedAssets_RemovesAssetFilters()
    {
        // Arrange
        var namespaceEntity = await CreateNamespace();
        var channel = CreateChannel(".NET 8", "release", namespaceEntity);
        await _context.Channels.AddAsync(channel);
        await _context.SaveChangesAsync();

        var subscriptionId = Guid.NewGuid();
        var existingSubscription = CreateSubscription(
            subscriptionId,
            channel,
            "https://github.com/dotnet/runtime",
            "https://github.com/dotnet/aspnetcore",
            "main",
            enabled: true,
            namespaceEntity,
            excludedAssets:
            [
                new AssetFilter { Filter = "Microsoft.NET.Sdk" },
                new AssetFilter { Filter = "Microsoft.AspNetCore.*" },
                new AssetFilter { Filter = "System.Text.Json" },
            ]);

        await _context.Subscriptions.AddAsync(existingSubscription);
        await _context.SaveChangesAsync();

        var updatedSubscription = new SubscriptionYaml
        {
            Id = subscriptionId,
            Channel = ".NET 8",
            SourceRepository = "https://github.com/dotnet/runtime",
            TargetRepository = "https://github.com/dotnet/aspnetcore",
            TargetBranch = "main",
            Enabled = true,
            ExcludedAssets =
            [
                "Microsoft.NET.Sdk"
            ],
        };

        var configData = new ConfigurationData(
            [updatedSubscription],
            [new ChannelYaml { Name = ".NET 8", Classification = "release" }],
            [],
            []);

        // Act
        var result = await _ingestor.IngestConfigurationAsync(configData, _testNamespace);

        // Assert
        result.Subscriptions.Updates.Should().HaveCount(1);

        var updated = await _context.Subscriptions
            .Include(s => s.ExcludedAssets)
            .FirstOrDefaultAsync(s => s.Id == subscriptionId);

        updated.Should().NotBeNull();
        updated.ExcludedAssets.Should().ContainSingle()
            .Which.Filter.Should().Be("Microsoft.NET.Sdk");
    }

    [Test]
    public async Task IngestConfigurationAsync_UpdateSubscriptionExcludedAssets_ClearsAllAssetFilters()
    {
        // Arrange
        var namespaceEntity = await CreateNamespace();
        var channel = CreateChannel(".NET 8", "release", namespaceEntity);
        await _context.Channels.AddAsync(channel);
        await _context.SaveChangesAsync();

        var subscriptionId = Guid.NewGuid();
        var existingSubscription = CreateSubscription(
            subscriptionId,
            channel,
            "https://github.com/dotnet/runtime",
            "https://github.com/dotnet/aspnetcore",
            "main",
            enabled: true,
            namespaceEntity,
            excludedAssets:
            [
                new AssetFilter { Filter = "Microsoft.NET.Sdk" },
                new AssetFilter { Filter = "Microsoft.AspNetCore.*" },
            ]);

        await _context.Subscriptions.AddAsync(existingSubscription);
        await _context.SaveChangesAsync();

        var updatedSubscription = new SubscriptionYaml
        {
            Id = subscriptionId,
            Channel = ".NET 8",
            SourceRepository = "https://github.com/dotnet/runtime",
            TargetRepository = "https://github.com/dotnet/aspnetcore",
            TargetBranch = "main",
            Enabled = true,
            ExcludedAssets = [],
        };

        var configData = new ConfigurationData(
            [updatedSubscription],
            [new ChannelYaml { Name = ".NET 8", Classification = "release" }],
            [],
            []);

        // Act
        var result = await _ingestor.IngestConfigurationAsync(configData, _testNamespace);

        // Assert
        result.Subscriptions.Updates.Should().HaveCount(1);

        var updated = await _context.Subscriptions
            .Include(s => s.ExcludedAssets)
            .FirstOrDefaultAsync(s => s.Id == subscriptionId);

        updated.Should().NotBeNull();
        updated.ExcludedAssets.Should().BeEmpty();
    }

    [Test]
    public async Task IngestConfigurationAsync_UpdateSubscriptionExcludedAssets_ComplexUpdate()
    {
        // Arrange
        var namespaceEntity = await CreateNamespace();
        var channel = CreateChannel(".NET 8", "release", namespaceEntity);
        await _context.Channels.AddAsync(channel);
        await _context.SaveChangesAsync();

        var subscriptionId = Guid.NewGuid();
        var existingSubscription = CreateSubscription(
            subscriptionId,
            channel,
            "https://github.com/dotnet/runtime",
            "https://github.com/dotnet/aspnetcore",
            "main",
            enabled: true,
            namespaceEntity,
            excludedAssets:
            [
                new AssetFilter { Filter = "ToKeep.Package" },
                new AssetFilter { Filter = "ToRemove.Package" },
                new AssetFilter { Filter = "ToModify.Package" },
            ]);

        await _context.Subscriptions.AddAsync(existingSubscription);
        await _context.SaveChangesAsync();

        var updatedSubscription = new SubscriptionYaml
        {
            Id = subscriptionId,
            Channel = ".NET 8",
            SourceRepository = "https://github.com/dotnet/runtime",
            TargetRepository = "https://github.com/dotnet/aspnetcore",
            TargetBranch = "main",
            Enabled = true,
            ExcludedAssets =
            [
                "ToKeep.Package",
                "ToModify.Package",
                "NewPackage.*"
                // ToRemove.Package is removed
            ],
        };

        var configData = new ConfigurationData(
            [updatedSubscription],
            [new ChannelYaml { Name = ".NET 8", Classification = "release" }],
            [],
            []);

        // Act
        var result = await _ingestor.IngestConfigurationAsync(configData, _testNamespace);

        // Assert
        result.Subscriptions.Updates.Should().HaveCount(1);

        var updated = await _context.Subscriptions
            .Include(s => s.ExcludedAssets)
            .FirstOrDefaultAsync(s => s.Id == subscriptionId);

        updated.Should().NotBeNull();
        updated.ExcludedAssets.Should().HaveCount(3);

        // Verify ToKeep.Package is unchanged
        updated.ExcludedAssets.Should().Contain(a => a.Filter == "ToKeep.Package");

        // Verify ToModify.Package exists
        updated.ExcludedAssets.Should().Contain(a => a.Filter == "ToModify.Package");

        // Verify NewPackage.* was added
        updated.ExcludedAssets.Should().Contain(a => a.Filter == "NewPackage.*");

        // Verify ToRemove.Package was removed
        updated.ExcludedAssets.Should().NotContain(a => a.Filter == "ToRemove.Package");
    }

    [Test]
    public async Task IngestConfigurationAsync_UpdateDefaultChannel_UpdatesEnabledStatus()
    {
        // Arrange
        var namespaceEntity = await CreateNamespace();
        var channel = CreateChannel(".NET 8", "release", namespaceEntity);
        await _context.Channels.AddAsync(channel);
        await _context.SaveChangesAsync();

        var defaultChannel = CreateDefaultChannel(
            channel,
            "https://github.com/dotnet/runtime",
            "main",
            enabled: true,
            namespaceEntity);

        await _context.DefaultChannels.AddAsync(defaultChannel);
        await _context.SaveChangesAsync();

        var updatedDefaultChannel = new DefaultChannelYaml
        {
            Repository = "https://github.com/dotnet/runtime",
            Branch = "main",
            Channel = ".NET 8",
            Enabled = false, // Changed from true to false
        };

        var configData = new ConfigurationData(
            [],
            [new ChannelYaml { Name = ".NET 8", Classification = "release" }],
            [updatedDefaultChannel],
            []);

        // Act
        var result = await _ingestor.IngestConfigurationAsync(configData, _testNamespace);

        // Assert
        result.DefaultChannels.Updates.Should().HaveCount(1);

        namespaceEntity = await _context.Namespaces
            .Include(n => n.DefaultChannels)
            .FirstOrDefaultAsync(n => n.Name == _testNamespace);

        namespaceEntity.Should().NotBeNull();
        var updated = namespaceEntity.DefaultChannels.FirstOrDefault();
        updated.Should().NotBeNull();
        updated.Enabled.Should().BeFalse();
    }

    [Test]
    public async Task IngestConfigurationAsync_UpdateRepositoryBranch_UpdatesPolicyString()
    {
        // Arrange
        var namespaceEntity = await CreateNamespace();

        var existingBranch = await CreateRepositoryBranch(
            "https://github.com/dotnet/runtime",
            "main",
            [new MergePolicyYaml { Name = "AllChecksSuccessful" }],
            namespaceEntity);

        await _context.RepositoryBranches.AddAsync(existingBranch);
        await _context.SaveChangesAsync();

        var updatedBranchYaml = new BranchMergePoliciesYaml
        {
            Repository = "https://github.com/dotnet/runtime",
            Branch = "main",
            MergePolicies =
            [
                new MergePolicyYaml { Name = "AllChecksSuccessful" },
                new MergePolicyYaml { Name = "RequireReviews" },
            ],
        };

        var configData = new ConfigurationData(
            [],
            [],
            [],
            [updatedBranchYaml]);

        // Act
        var result = await _ingestor.IngestConfigurationAsync(configData, _testNamespace);

        // Assert
        result.RepositoryBranches.Updates.Should().HaveCount(1);

        namespaceEntity = await _context.Namespaces
            .Include(n => n.RepositoryBranches)
            .FirstOrDefaultAsync(n => n.Name == _testNamespace);

        namespaceEntity.Should().NotBeNull();
        var updated = namespaceEntity.RepositoryBranches
            .FirstOrDefault(rb => rb.RepositoryName == "https://github.com/dotnet/runtime" && rb.BranchName == "main");

        updated.Should().NotBeNull();
        updated.PolicyObject.MergePolicies.Should().HaveCount(2);
    }

    #endregion

    #region Deletion Tests

    [Test]
    public async Task IngestConfigurationAsync_RemoveChannel_DeletesChannel()
    {
        // Arrange
        await CreateNamespace();
        var namespaceEntity = await _context.Namespaces.FirstAsync(n => n.Name == _testNamespace);
        var channel = CreateChannel("Old Channel", "dev", namespaceEntity);
        await _context.Channels.AddAsync(channel);
        await _context.SaveChangesAsync();

        var configData = new ConfigurationData([], [], [], []);

        // Act
        var result = await _ingestor.IngestConfigurationAsync(configData, _testNamespace);

        // Assert
        result.Channels.Removals.Should().HaveCount(1);

        var updatedNamespace = await _context.Namespaces
            .Include(n => n.Channels)
            .FirstOrDefaultAsync(n => n.Name == _testNamespace);

        updatedNamespace.Should().NotBeNull();
        updatedNamespace.Channels.Should().BeEmpty();
    }


    [Test]
    public async Task IngestConfigurationAsync_RemoveChannel_FailToAddSubscription()
    {
        // Arrange
        var namespaceEntity = await CreateNamespace();
        var channel = CreateChannel("Old Channel", "dev", namespaceEntity);
        await _context.Channels.AddAsync(channel);
        await _context.SaveChangesAsync();

        var subscription =
        new SubscriptionYaml
        {
            Id = Guid.NewGuid(),
            Channel = "Old channel",
            SourceRepository = "https://github.com/dotnet/runtime",
            TargetRepository = "https://github.com/dotnet/aspnetcore",
            TargetBranch = "main",
            Enabled = true,
        };

        var configData = new ConfigurationData([subscription], [], [], []);

        var act = async () => await _ingestor.IngestConfigurationAsync(configData, _testNamespace);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Test]
    public async Task IngestConfigurationAsync_UpdateChannel_FailToUpdateName()
    {
        var namespaceEntity = await CreateNamespace();
        var channel = CreateChannel("Old Channel", "dev", namespaceEntity);

        var id = Guid.NewGuid();

        var subscription = CreateSubscription(
            id,
            channel,
            "https://github.com/dotnet/runtime",
            "https://github.com/dotnet/aspnetcore",
            "main",
            enabled: true,
            namespaceEntity);

        await _context.Channels.AddAsync(channel);
        await _context.Subscriptions.AddAsync(subscription);
        await _context.SaveChangesAsync();

        var updatedChannelYaml = new ChannelYaml
        {
            Name = "New Channel",
            Classification = "production",
        };

        var subscriptionYaml = new SubscriptionYaml
        {
            Id = id,
            Channel = "New Channel",
            SourceRepository = "https://github.com/dotnet/runtime",
            TargetRepository = "https://github.com/dotnet/aspnetcore",
            TargetBranch = "main",
            Enabled = true,
        };

        var configData = new ConfigurationData(
            [subscriptionYaml],
            [updatedChannelYaml],
            [],
            []);

        // Assert
        // Channel names are immutable.
        // During ingestion, the channel name update results in channel removal + creation.
        // This should fail because the channel is still referenced by the existing subscription in the DB.
        var act = async () => await _ingestor.IngestConfigurationAsync(configData, _testNamespace);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Test]
    public async Task IngestConfigurationAsync_RemoveSubscription_DeletesSubscription()
    {
        // Arrange
        var namespaceEntity = await CreateNamespace();
        var channel = CreateChannel(".NET 8", "release", namespaceEntity);
        await _context.Channels.AddAsync(channel);
        await _context.SaveChangesAsync();

        var subscription = CreateSubscription(
            Guid.NewGuid(),
            channel,
            "https://github.com/dotnet/runtime",
            "https://github.com/dotnet/aspnetcore",
            "main",
            enabled: true,
            namespaceEntity);

        await _context.Subscriptions.AddAsync(subscription);
        await _context.SaveChangesAsync();

        var configData = new ConfigurationData(
            [],
            [new ChannelYaml { Name = ".NET 8", Classification = "release" }],
            [],
            []);

        // Act
        var result = await _ingestor.IngestConfigurationAsync(configData, _testNamespace);

        // Assert
        result.Subscriptions.Removals.Should().HaveCount(1);

        namespaceEntity = await _context.Namespaces
            .Include(n => n.Subscriptions)
            .FirstOrDefaultAsync(n => n.Name == _testNamespace);

        namespaceEntity.Should().NotBeNull();
        namespaceEntity.Subscriptions.Should().BeEmpty();
    }

    [Test]
    public async Task IngestConfigurationAsync_RemoveDefaultChannel_DeletesDefaultChannel()
    {
        // Arrange
        var namespaceEntity = await CreateNamespace();
        var channel = CreateChannel(".NET 8", "release", namespaceEntity);
        await _context.Channels.AddAsync(channel);
        await _context.SaveChangesAsync();

        var defaultChannel = CreateDefaultChannel(
            channel,
            "https://github.com/dotnet/runtime",
            "main",
            enabled: true,
            namespaceEntity);

        await _context.DefaultChannels.AddAsync(defaultChannel);
        await _context.SaveChangesAsync();

        var configData = new ConfigurationData(
            [],
            [new ChannelYaml { Name = ".NET 8", Classification = "release" }],
            [],
            []);

        // Act
        var result = await _ingestor.IngestConfigurationAsync(configData, _testNamespace);

        // Assert
        result.DefaultChannels.Removals.Should().HaveCount(1);

        namespaceEntity = await _context.Namespaces
            .Include(n => n.DefaultChannels)
            .FirstOrDefaultAsync(n => n.Name == _testNamespace);

        namespaceEntity.Should().NotBeNull();
        namespaceEntity.DefaultChannels.Should().BeEmpty();
    }

    [Test]
    public async Task IngestConfigurationAsync_RemoveRepositoryBranch_DeletesRepositoryBranch()
    {
        // Arrange
        var namespaceEntity = await CreateNamespace();

        var branch = await CreateRepositoryBranch(
            "https://github.com/dotnet/runtime",
            "main",
            [new MergePolicyYaml { Name = "AllChecksSuccessful" }],
            namespaceEntity);

        await _context.RepositoryBranches.AddAsync(branch);
        await _context.SaveChangesAsync();

        var configData = new ConfigurationData([], [], [], []);

        // Act
        var result = await _ingestor.IngestConfigurationAsync(configData, _testNamespace);

        // Assert
        result.RepositoryBranches.Removals.Should().HaveCount(1);

        namespaceEntity = await _context.Namespaces
            .Include(n => n.RepositoryBranches)
            .FirstOrDefaultAsync(n => n.Name == _testNamespace);

        namespaceEntity.Should().NotBeNull();
        namespaceEntity.RepositoryBranches.Should().BeEmpty();
    }

    #endregion

    #region Mixed Operations Tests

    [Test]
    public async Task IngestConfigurationAsync_MixedOperations_HandlesAllCorrectly()
    {
        // Arrange
        var namespaceEntity = await CreateNamespace();

        // Existing entities
        var existingChannel = CreateChannel("Existing Channel", "dev", namespaceEntity);
        var channelToUpdate = CreateChannel("Channel To Update", "dev", namespaceEntity);
        var channelToDelete = CreateChannel("Channel To Delete", "dev", namespaceEntity);

        await _context.Channels.AddRangeAsync(existingChannel, channelToUpdate, channelToDelete);
        await _context.SaveChangesAsync();

        // Configuration with mixed operations
        var configData = new ConfigurationData(
            [],
            [
                new ChannelYaml { Name = "Existing Channel", Classification = "dev" }, // No change
                new ChannelYaml { Name = "Channel To Update", Classification = "production" }, // Update
                new ChannelYaml { Name = "New Channel", Classification = "test" }, // Create
                // "Channel To Delete" is not included - will be deleted
            ],
            [],
            []);

        // Act
        var result = await _ingestor.IngestConfigurationAsync(configData, _testNamespace);

        // Assert
        result.Channels.Creations.Should().HaveCount(1);
        result.Channels.Updates.Should().HaveCount(2); // Both existing and updated
        result.Channels.Removals.Should().HaveCount(1);

        namespaceEntity = await _context.Namespaces
            .Include(n => n.Channels)
            .FirstOrDefaultAsync(n => n.Name == _testNamespace);

        namespaceEntity.Should().NotBeNull();
        var channels = namespaceEntity.Channels;
        channels.Should().HaveCount(3);
        channels.Should().Contain(c => c.Name == "New Channel");
        channels.Should().NotContain(c => c.Name == "Channel To Delete");

        var updated = channels.First(c => c.Name == "Channel To Update");
        updated.Classification.Should().Be("production");
    }

    [Test]
    public async Task IngestConfigurationAsync_ComplexScenario_HandlesAllEntities()
    {
        // Arrange
        var namespaceEntity = await CreateNamespace();

        // Setup existing state
        var existingChannel = CreateChannel(".NET 8", "release", namespaceEntity);
        var oldChannel = CreateChannel(".NET 9", "dev", namespaceEntity);
        await _context.Channels.AddRangeAsync(existingChannel, oldChannel);
        await _context.SaveChangesAsync();

        var oldSubscription = CreateSubscription(
            Guid.NewGuid(),
            oldChannel,
            "https://github.com/dotnet/old",
            "https://github.com/dotnet/target",
            "main",
            enabled: true,
            namespaceEntity);

        await _context.Subscriptions.AddAsync(oldSubscription);
        await _context.SaveChangesAsync();

        // New configuration
        var newSubscriptionId = Guid.NewGuid();
        var configData = new ConfigurationData(
            [
                new SubscriptionYaml
                {
                    Id = newSubscriptionId,
                    Channel = ".NET 8",
                    SourceRepository = "https://github.com/dotnet/runtime",
                    TargetRepository = "https://github.com/dotnet/aspnetcore",
                    TargetBranch = "main",
                    Enabled = true,
                },
            ],
            [
                new ChannelYaml { Name = ".NET 8", Classification = "release" },
                new ChannelYaml { Name = ".NET 9", Classification = "preview" },
            ],
            [
                new DefaultChannelYaml
                {
                    Repository = "https://github.com/dotnet/runtime",
                    Branch = "main",
                    Channel = ".NET 8",
                    Enabled = true,
                },
            ],
            [
                new BranchMergePoliciesYaml
                {
                    Repository = "https://github.com/dotnet/runtime",
                    Branch = "main",
                    MergePolicies = [new MergePolicyYaml { Name = "AllChecksSuccessful" }],
                },
            ]);

        // Act
        var result = await _ingestor.IngestConfigurationAsync(configData, _testNamespace);

        // Assert
        result.Channels.Updates.Should().HaveCount(2); // .NET 8

        result.Subscriptions.Creations.Should().HaveCount(1);
        result.Subscriptions.Removals.Should().HaveCount(1);

        result.DefaultChannels.Creations.Should().HaveCount(1);
        result.RepositoryBranches.Creations.Should().HaveCount(1);

        namespaceEntity = await _context.Namespaces
            .Include(n => n.Channels)
            .FirstOrDefaultAsync(n => n.Name == _testNamespace);

        namespaceEntity.Should().NotBeNull();
        var channels = namespaceEntity.Channels;
        channels.Should().HaveCount(2);
        channels.Should().Contain(c => c.Name == ".NET 9");
    }

    #endregion

    #region Namespace Tests

    [Test]
    public async Task IngestConfigurationAsync_ExistingNamespace_ReusesNamespace()
    {
        // Arrange
        var existingNamespace = new Namespace { Name = _testNamespace };
        await _context.Namespaces.AddAsync(existingNamespace);
        await _context.SaveChangesAsync();

        var configData = CreateBasicConfigurationData();

        // Act
        await _ingestor.IngestConfigurationAsync(configData, _testNamespace);

        // Assert
        var namespaceEntity = await _context.Namespaces
            .FirstOrDefaultAsync(n => n.Name == _testNamespace);

        namespaceEntity.Should().NotBeNull();
    }

    [Test]
    public async Task IngestConfigurationAsync_DifferentNamespaces_IsolatesData()
    {
        // Arrange
        var namespace1 = "namespace1";
        var namespace2 = "namespace2";

        var configData1 = new ConfigurationData(
            [],
            [new ChannelYaml { Name = "Channel 1", Classification = "dev" }],
            [],
            []);

        var configData2 = new ConfigurationData(
            [],
            [new ChannelYaml { Name = "Channel 2", Classification = "release" }],
            [],
            []);

        // Act
        await _ingestor.IngestConfigurationAsync(configData1, namespace1);
        await _ingestor.IngestConfigurationAsync(configData2, namespace2);

        // Assert
        var namespaces = await _context.Namespaces
            .Where(n => n.Name == namespace1 || n.Name == namespace2)
            .Include(n => n.Channels)
            .ToListAsync();

        var channels = await _context.Channels.ToListAsync();
        namespaces.SelectMany(n => n.Channels).Should().HaveCount(2);

        var ns1 = namespaces.First(n => n.Name == namespace1);
        var ns2 = namespaces.First(n => n.Name == namespace2);

        ns1.Channels.Should().Contain(c => c.Name == "Channel 1");
        ns2.Channels.Should().Contain(c => c.Name == "Channel 2");
    }

    #endregion

    #region Helper Methods

    private async Task<Namespace> CreateNamespace()
    {
        var namespaceEntity = new Namespace { Name = _testNamespace };
        await _context.Namespaces.AddAsync(namespaceEntity);
        await _context.SaveChangesAsync();
        return namespaceEntity;
    }

    private async Task EnsureRepositoriesExist(params string[] repositoryNames)
    {
        foreach (var repositoryName in repositoryNames)
        {
            var exists = await _context.Repositories.AnyAsync(r => r.RepositoryName == repositoryName);
            if (!exists)
            {
                await _context.Repositories.AddAsync(new Repository { RepositoryName = repositoryName });
            }
        }
        await _context.SaveChangesAsync();
    }

    private static Channel CreateChannel(string name, string classification, Namespace namespaceEntity)
        => new()
        {
            Name = name,
            Classification = classification,
            Namespace = namespaceEntity,
        };

    private static Subscription CreateSubscription(
        Guid id,
        Channel channel,
        string sourceRepo,
        string targetRepo,
        string targetBranch,
        bool enabled,
        Namespace namespaceEntity,
        List<AssetFilter>? excludedAssets = null)
        => new()
        {
            Id = id,
            Channel = channel,
            ChannelId = channel.Id,
            SourceRepository = sourceRepo,
            TargetRepository = targetRepo,
            TargetBranch = targetBranch,
            Enabled = enabled,
            PolicyObject = new SubscriptionPolicy
            {
                UpdateFrequency = UpdateFrequency.EveryBuild,
                Batchable = false,
                MergePolicies = [],
            },
            Namespace = namespaceEntity,
            ExcludedAssets = excludedAssets ?? [],
        };

    private static DefaultChannel CreateDefaultChannel(
        Channel channel,
        string repository,
        string branch,
        bool enabled,
        Namespace namespaceEntity)
        => new()
        {
            Channel = channel,
            ChannelId = channel.Id,
            Repository = repository,
            Branch = branch,
            Enabled = enabled,
            Namespace = namespaceEntity,
        };

    private async Task<RepositoryBranch> CreateRepositoryBranch(
        string repositoryName,
        string branchName,
        List<MergePolicyYaml> mergePolicies,
        Namespace namespaceEntity)
    {
        var repository = await _context.Repositories.FirstOrDefaultAsync(r => r.RepositoryName == repositoryName);
        if (repository == null)
        {
            repository = new Repository { RepositoryName = repositoryName };
            await _context.Repositories.AddAsync(repository);
            await _context.SaveChangesAsync();
        }

        var policyObject = new RepositoryBranch.Policy
        {
            MergePolicies = mergePolicies
                .Select(mp => new MergePolicyDefinition
                {
                    Name = mp.Name,
                    Properties = mp.Properties?.ToDictionary(p => p.Key, p => Newtonsoft.Json.Linq.JToken.FromObject(p.Value)),
                })
                .ToList(),
        };

        return new RepositoryBranch
        {
            Repository = repository,
            RepositoryName = repository.RepositoryName,
            BranchName = branchName,
            PolicyString = Newtonsoft.Json.JsonConvert.SerializeObject(policyObject),
            Namespace = namespaceEntity,
        };
    }

    private static ConfigurationData CreateBasicConfigurationData()
    {
        var channelYaml = new ChannelYaml
        {
            Name = ".NET 8",
            Classification = "release",
        };

        var subscriptionYaml = new SubscriptionYaml
        {
            Id = Guid.NewGuid(),
            Channel = ".NET 8",
            SourceRepository = "https://github.com/dotnet/runtime3",
            TargetRepository = "https://github.com/dotnet/aspnetcore",
            TargetBranch = "main",
            Enabled = true,
        };

        var defaultChannelYaml = new DefaultChannelYaml
        {
            Repository = "https://github.com/dotnet/runtime",
            Branch = "main",
            Channel = ".NET 8",
            Enabled = true,
        };

        var branchMergePoliciesYaml = new BranchMergePoliciesYaml
        {
            Repository = "https://github.com/dotnet/runtime2",
            Branch = "main",
            MergePolicies = [new MergePolicyYaml { Name = "AllChecksSuccessful" }],
        };

        return new ConfigurationData(
            [subscriptionYaml],
            [channelYaml],
            [defaultChannelYaml],
            [branchMergePoliciesYaml]);
    }

    #endregion
}
