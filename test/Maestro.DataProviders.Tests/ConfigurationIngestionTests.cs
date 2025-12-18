// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data;
using Maestro.Data.Models;
using Maestro.DataProviders.ConfigurationIngestion;
using Microsoft.DotNet.MaestroConfiguration.Client.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProductConstructionService.Api.Tests;

#nullable enable
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
        _testDatabase = new TestDatabase("TestDB_Ingestion_" + TestContext.CurrentContext.Test.MethodName);
        var connectionString = await _testDatabase.GetConnectionString();

        var options = new DbContextOptionsBuilder<BuildAssetRegistryContext>()
            .UseSqlServer(connectionString)
            .Options;

        _context = new BuildAssetRegistryContext(options);

        var services = new ServiceCollection()
            .AddSingleton(_context)
            .AddSingleton<ISqlBarClient>(new SqlBarClient(_context, null))
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
        Assert.That(result.Channels.Creations.Count, Is.EqualTo(1));
        Assert.That(result.Channels.Updates.Count, Is.EqualTo(0));
        Assert.That(result.Channels.Removals.Count, Is.EqualTo(0));

        Assert.That(result.Subscriptions.Creations.Count, Is.EqualTo(1));
        Assert.That(result.Subscriptions.Updates.Count, Is.EqualTo(0));
        Assert.That(result.Subscriptions.Removals.Count, Is.EqualTo(0));

        Assert.That(result.DefaultChannels.Creations.Count, Is.EqualTo(1));
        Assert.That(result.DefaultChannels.Updates.Count, Is.EqualTo(0));
        Assert.That(result.DefaultChannels.Removals.Count, Is.EqualTo(0));

        Assert.That(result.RepositoryBranches.Creations.Count, Is.EqualTo(1));
        Assert.That(result.RepositoryBranches.Updates.Count, Is.EqualTo(0));
        Assert.That(result.RepositoryBranches.Removals.Count, Is.EqualTo(0));

        var channels = await _context.Channels.ToListAsync();
        Assert.That(channels, Has.Count.EqualTo(1));
        Assert.That(channels[0].Name, Is.EqualTo(".NET 8"));
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

        Assert.That(namespaceEntity, Is.Not.Null);
        Assert.That(namespaceEntity!.Name, Is.EqualTo(_testNamespace));
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
        Assert.That(result.Channels.Updates.Count, Is.EqualTo(1));
        Assert.That(result.Channels.Creations.Count, Is.EqualTo(0));
        Assert.That(result.Channels.Removals.Count, Is.EqualTo(0));

        var updatedChannel = await _context.Channels
            .FirstOrDefaultAsync(c => c.Name == "Test Channel");

        Assert.That(updatedChannel, Is.Not.Null);
        Assert.That(updatedChannel!.Classification, Is.EqualTo("production"));
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

        Assert.That(result.Subscriptions.Updates.Count, Is.EqualTo(1));
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
        Assert.That(result.Subscriptions.Updates.Count, Is.EqualTo(1));

        var updated = await _context.Subscriptions
            .Include(s => s.ExcludedAssets)
            .FirstOrDefaultAsync(s => s.Id == subscriptionId);

        Assert.That(updated, Is.Not.Null);
        Assert.That(updated!.ExcludedAssets, Has.Count.EqualTo(3));
        Assert.That(updated.ExcludedAssets.Any(a => a.Filter == "Microsoft.NET.Sdk"), Is.True);
        Assert.That(updated.ExcludedAssets.Any(a => a.Filter == "Microsoft.AspNetCore.*"), Is.True);
        Assert.That(updated.ExcludedAssets.Any(a => a.Filter == "System.Text.Json"), Is.True);
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
        Assert.That(result.Subscriptions.Updates.Count, Is.EqualTo(1));

        var updated = await _context.Subscriptions
            .Include(s => s.ExcludedAssets)
            .FirstOrDefaultAsync(s => s.Id == subscriptionId);

        Assert.That(updated, Is.Not.Null);
        Assert.That(updated!.ExcludedAssets, Has.Count.EqualTo(1));
        Assert.That(updated.ExcludedAssets.First().Filter, Is.EqualTo("Microsoft.NET.Sdk"));
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
        Assert.That(result.Subscriptions.Updates.Count, Is.EqualTo(1));

        var updated = await _context.Subscriptions
            .Include(s => s.ExcludedAssets)
            .FirstOrDefaultAsync(s => s.Id == subscriptionId);

        Assert.That(updated, Is.Not.Null);
        Assert.That(updated!.ExcludedAssets, Is.Empty);
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
        Assert.That(result.Subscriptions.Updates.Count, Is.EqualTo(1));

        var updated = await _context.Subscriptions
            .Include(s => s.ExcludedAssets)
            .FirstOrDefaultAsync(s => s.Id == subscriptionId);

        Assert.That(updated, Is.Not.Null);
        Assert.That(updated!.ExcludedAssets, Has.Count.EqualTo(3));

        // Verify ToKeep.Package is unchanged
        var toKeep = updated.ExcludedAssets.FirstOrDefault(a => a.Filter == "ToKeep.Package");
        Assert.That(toKeep, Is.Not.Null);

        // Verify ToModify.Package has updated NonShipping flag
        var toModify = updated.ExcludedAssets.FirstOrDefault(a => a.Filter == "ToModify.Package");
        Assert.That(toModify, Is.Not.Null);

        // Verify NewPackage.* was added
        var newPackage = updated.ExcludedAssets.FirstOrDefault(a => a.Filter == "NewPackage.*");
        Assert.That(newPackage, Is.Not.Null);

        // Verify ToRemove.Package was removed
        Assert.That(updated.ExcludedAssets.Any(a => a.Filter == "ToRemove.Package"), Is.False);
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
        Assert.That(result.DefaultChannels.Updates.Count, Is.EqualTo(1));

        var updated = await _context.DefaultChannels.FirstOrDefaultAsync(dc => dc.Id == 1);
        Assert.That(updated, Is.Not.Null);
        Assert.That(updated!.Enabled, Is.False);
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
        Assert.That(result.RepositoryBranches.Updates.Count, Is.EqualTo(1));

        var updated = await _context.RepositoryBranches
            .FirstOrDefaultAsync(rb => rb.RepositoryName == "https://github.com/dotnet/runtime" && rb.BranchName == "main");

        Assert.That(updated, Is.Not.Null);
        Assert.That(updated!.PolicyObject.MergePolicies, Has.Count.EqualTo(2));
    }

    #endregion

    #region Deletion Tests

    [Test]
    public async Task IngestConfigurationAsync_RemoveChannel_DeletesChannel()
    {
        // Arrange
        var namespaceEntity = await CreateNamespace();
        var channel = CreateChannel("Old Channel", "dev", namespaceEntity);
        await _context.Channels.AddAsync(channel);
        await _context.SaveChangesAsync();

        var configData = new ConfigurationData([], [], [], []);

        // Act
        var result = await _ingestor.IngestConfigurationAsync(configData, _testNamespace);

        // Assert
        Assert.That(result.Channels.Removals.Count, Is.EqualTo(1));

        var channels = await _context.Channels.ToListAsync();
        Assert.That(channels, Is.Empty);
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

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            var result = await _ingestor.IngestConfigurationAsync(configData, _testNamespace);
        });
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
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            var result = await _ingestor.IngestConfigurationAsync(configData, _testNamespace);
        });
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
        Assert.That(result.Subscriptions.Removals.Count, Is.EqualTo(1));

        var subscriptions = await _context.Subscriptions.ToListAsync();
        Assert.That(subscriptions, Is.Empty);
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
        Assert.That(result.DefaultChannels.Removals.Count, Is.EqualTo(1));

        var defaultChannels = await _context.DefaultChannels.ToListAsync();
        Assert.That(defaultChannels, Is.Empty);
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
        Assert.That(result.RepositoryBranches.Removals.Count, Is.EqualTo(1));

        var branches = await _context.RepositoryBranches.ToListAsync();
        Assert.That(branches, Is.Empty);
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
        Assert.That(result.Channels.Creations.Count, Is.EqualTo(1));
        Assert.That(result.Channels.Updates.Count, Is.EqualTo(2)); // Both existing and updated
        Assert.That(result.Channels.Removals.Count, Is.EqualTo(1));

        var channels = await _context.Channels.ToListAsync();
        Assert.That(channels, Has.Count.EqualTo(3));
        Assert.That(channels.Any(c => c.Name == "New Channel"), Is.True);
        Assert.That(channels.Any(c => c.Name == "Channel To Delete"), Is.False);

        var updated = channels.First(c => c.Name == "Channel To Update");
        Assert.That(updated.Classification, Is.EqualTo("production"));
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
        Assert.That(result.Channels.Updates.Count, Is.EqualTo(2)); // .NET 8

        Assert.That(result.Subscriptions.Creations.Count, Is.EqualTo(1));
        Assert.That(result.Subscriptions.Removals.Count, Is.EqualTo(1));

        Assert.That(result.DefaultChannels.Creations.Count, Is.EqualTo(1));
        Assert.That(result.RepositoryBranches.Creations.Count, Is.EqualTo(1));

        var channels = await _context.Channels.ToListAsync();
        Assert.That(channels, Has.Count.EqualTo(2));
        Assert.That(channels.Any(c => c.Name == ".NET 9"), Is.True);
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
        var namespaces = await _context.Namespaces.ToListAsync();
        Assert.That(namespaces, Has.Count.EqualTo(2)); // 1 new namespace + the original one (created by DB migration)
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
        var namespaces = await _context.Namespaces.ToListAsync();
        Assert.That(namespaces, Has.Count.EqualTo(3)); // 2 new namespaces + the original one (created by DB migration)

        var channels = await _context.Channels.ToListAsync();
        Assert.That(channels, Has.Count.EqualTo(2));

        var ns1 = namespaces.First(n => n.Name == namespace1);
        var ns2 = namespaces.First(n => n.Name == namespace2);

        var channel1 = channels.First(c => c.Name == "Channel 1");
        var channel2 = channels.First(c => c.Name == "Channel 2");

        Assert.That(channel1.Namespace.Name, Is.EqualTo(namespace1));
        Assert.That(channel2.Namespace.Name, Is.EqualTo(namespace2));
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
