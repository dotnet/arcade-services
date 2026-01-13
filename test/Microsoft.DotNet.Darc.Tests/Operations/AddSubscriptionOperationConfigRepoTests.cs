// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AwesomeAssertions;
using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.MaestroConfiguration.Client;
using Microsoft.DotNet.MaestroConfiguration.Client.Models;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.Darc.Tests.Operations;

[TestFixture]
public class AddSubscriptionOperationConfigRepoTests : ConfigurationManagementTestBase
{
    private Mock<ILogger<AddSubscriptionOperation>> _loggerMock = null!;

    [SetUp]
    public override async Task SetupAsync()
    {
        await base.SetupAsync();
        _loggerMock = new Mock<ILogger<AddSubscriptionOperation>>();
    }

    [Test]
    public async Task AddSubscriptionOperation_WithConfigRepo_CreatesSubscriptionFile()
    {
        // Arrange - Define expected subscription first
        var expectedSubscription = new SubscriptionYaml
        {
            Id = Guid.NewGuid(),
            Channel = "test-channel",
            SourceRepository = "https://github.com/dotnet/source-repo",
            TargetRepository = "https://github.com/dotnet/target-repo",
            TargetBranch = "main",
            UpdateFrequency = UpdateFrequency.EveryDay,
            Enabled = true
        };
        var testBranch = GetTestBranch();
        var expectedFilePath = ConfigFilePathResolver.GetDefaultSubscriptionFilePath(expectedSubscription);

        SetupChannel(expectedSubscription.Channel);

        var options = CreateAddSubscriptionOptions(expectedSubscription, configurationBranch: testBranch);
        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);

        // Verify file was created at the expected path
        await CheckoutBranch(testBranch);
        var fullExpectedPath = Path.Combine(ConfigurationRepoPath, expectedFilePath.ToString());
        File.Exists(fullExpectedPath).Should().BeTrue($"Expected file at {fullExpectedPath}");

        // Deserialize and verify subscription properties
        var subscriptions = await DeserializeSubscriptionsAsync(fullExpectedPath);
        subscriptions.Should().HaveCount(1);

        var actualSubscription = subscriptions[0];
        actualSubscription.Channel.Should().Be(expectedSubscription.Channel);
        actualSubscription.SourceRepository.Should().Be(expectedSubscription.SourceRepository);
        actualSubscription.TargetRepository.Should().Be(expectedSubscription.TargetRepository);
        actualSubscription.TargetBranch.Should().Be(expectedSubscription.TargetBranch);
        actualSubscription.UpdateFrequency.Should().Be(expectedSubscription.UpdateFrequency);
        actualSubscription.Enabled.Should().Be(expectedSubscription.Enabled);
    }

    [Test]
    public async Task AddSubscriptionOperation_WithConfigRepo_AppendsToExistingFile()
    {
        // Arrange - Define expected subscription first
        var expectedSubscription = new SubscriptionYaml
        {
            Id = Guid.NewGuid(),
            Channel = "test-channel",
            SourceRepository = "https://github.com/dotnet/source-repo",
            TargetRepository = "https://github.com/dotnet/target-repo",
            TargetBranch = "main",
            UpdateFrequency = UpdateFrequency.EveryDay,
            Enabled = true
        };
        var testBranch = GetTestBranch();

        const string existingSubscriptionId = "12345678-1234-1234-1234-123456789012";
        const string configFileName = "dotnet-target-repo.yml";

        // Create existing subscription file at the expected location
        var existingContent = $"""
            - Id: {existingSubscriptionId}
              Channel: existing-channel
              Source Repository URL: https://github.com/dotnet/existing-source
              Target Repository URL: {expectedSubscription.TargetRepository}
              Target Branch: release/1.0
              Update Frequency: EveryDay
            """;
        var configFilePath = new UnixPath(ConfigFilePathResolver.SubscriptionFolderPath) / configFileName;
        await CreateFileInConfigRepoAsync(configFilePath.ToString(), existingContent);

        SetupChannel(expectedSubscription.Channel);

        var options = CreateAddSubscriptionOptions(
            expectedSubscription,
            configurationBranch: testBranch);

        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);

        // Deserialize and verify both subscriptions are present
        await CheckoutBranch(testBranch);
        var fullPath = Path.Combine(ConfigurationRepoPath, configFilePath.ToString());
        var subscriptions = await DeserializeSubscriptionsAsync(fullPath);
        subscriptions.Should().HaveCount(2);

        subscriptions.Should().Contain(s => s.Id == Guid.Parse(existingSubscriptionId));
        subscriptions.Should().Contain(s => s.SourceRepository == expectedSubscription.SourceRepository);
    }

    [Test]
    public async Task AddSubscriptionOperation_WithConfigRepo_FailsWhenEquivalentSubscriptionExists()
    {
        // Arrange - Define expected subscription first
        var expectedSubscription = new SubscriptionYaml
        {
            Id = Guid.NewGuid(),
            Channel = "test-channel",
            SourceRepository = "https://github.com/dotnet/source-repo",
            TargetRepository = "https://github.com/dotnet/target-repo",
            TargetBranch = "main",
            UpdateFrequency = UpdateFrequency.EveryDay,
            Enabled = true
        };

        const int channelId = 42;
        SetupChannel(expectedSubscription.Channel, channelId: channelId);

        // Setup an existing equivalent subscription returned by BAR
        var existingSubscription = new Subscription(
            id: Guid.NewGuid(),
            enabled: true,
            sourceEnabled: false,
            sourceRepository: expectedSubscription.SourceRepository,
            targetRepository: expectedSubscription.TargetRepository,
            targetBranch: expectedSubscription.TargetBranch,
            pullRequestFailureNotificationTags: null,
            sourceDirectory: null,
            targetDirectory: null,
            excludedAssets: [])
        {
            Channel = new Channel(channelId, expectedSubscription.Channel, "test"),
            Policy = new SubscriptionPolicy(false, UpdateFrequency.EveryDay)
            {
                MergePolicies = []
            }
        };

        BarClientMock
            .Setup(x => x.GetSubscriptionsAsync(
                expectedSubscription.SourceRepository,
                expectedSubscription.TargetRepository,
                channelId,
                false,
                It.IsAny<string?>(),
                It.IsAny<string?>()))
            .ReturnsAsync([existingSubscription]);

        var options = CreateAddSubscriptionOptions(expectedSubscription, configurationBranch: GetTestBranch());
        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.ErrorCode);

        // No new yml files should have been created (only the README.md from setup)
        var ymlFiles = Directory.GetFiles(ConfigurationRepoPath, "*.yml", SearchOption.AllDirectories);
        ymlFiles.Should().BeEmpty();
    }

    [Test]
    public async Task AddSubscriptionOperation_WithConfigRepo_FileContentIsValidYaml()
    {
        // Arrange - Define expected subscription first
        var expectedSubscription = new SubscriptionYaml
        {
            Id = Guid.NewGuid(),
            Channel = ".NET 8",
            SourceRepository = "https://github.com/dotnet/runtime",
            TargetRepository = "https://github.com/dotnet/sdk",
            TargetBranch = "release/8.0",
            UpdateFrequency = UpdateFrequency.EveryDay,
            Enabled = true
        };

        var expectedFilePath = ConfigFilePathResolver.GetDefaultSubscriptionFilePath(expectedSubscription);

        SetupChannel(expectedSubscription.Channel);

        var options = CreateAddSubscriptionOptions(expectedSubscription, configurationBranch: GetTestBranch());
        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);

        // Verify file was created at the expected path
        var fullExpectedPath = Path.Combine(ConfigurationRepoPath, expectedFilePath.ToString());
        File.Exists(fullExpectedPath).Should().BeTrue($"Expected file at {fullExpectedPath}");

        // Deserialize and verify subscription properties match expected values
        var subscriptions = await DeserializeSubscriptionsAsync(fullExpectedPath);
        subscriptions.Should().HaveCount(1);

        var actualSubscription = subscriptions[0];
        actualSubscription.Id.Should().NotBeEmpty();
        actualSubscription.Channel.Should().Be(expectedSubscription.Channel);
        actualSubscription.SourceRepository.Should().Be(expectedSubscription.SourceRepository);
        actualSubscription.TargetRepository.Should().Be(expectedSubscription.TargetRepository);
        actualSubscription.TargetBranch.Should().Be(expectedSubscription.TargetBranch);
        actualSubscription.UpdateFrequency.Should().Be(expectedSubscription.UpdateFrequency);
    }

    [Test]
    public async Task AddSubscriptionOperation_WithConfigRepo_FailsWhenEquivalentSubscriptionExistsInYamlFile()
    {
        // Arrange - Define the subscription we want to add
        var subscriptionToAdd = new SubscriptionYaml
        {
            Id = Guid.NewGuid(),
            Channel = "test-channel",
            SourceRepository = "https://github.com/dotnet/source-repo",
            TargetRepository = "https://github.com/dotnet/target-repo",
            TargetBranch = "main",
            UpdateFrequency = UpdateFrequency.EveryDay,
            Enabled = true
        };

        const string existingSubscriptionId = "12345678-1234-1234-1234-123456789012";
        const string configFileName = "dotnet-target-repo.yml";

        // Create existing subscription file with an equivalent subscription (same source, target, branch, channel)
        var existingContent = $"""
            - Id: {existingSubscriptionId}
              Channel: {subscriptionToAdd.Channel}
              Source Repository URL: {subscriptionToAdd.SourceRepository}
              Target Repository URL: {subscriptionToAdd.TargetRepository}
              Target Branch: {subscriptionToAdd.TargetBranch}
              Update Frequency: EveryDay
            """;
        var configFilePath = new UnixPath(ConfigFilePathResolver.SubscriptionFolderPath) / configFileName;
        await CreateFileInConfigRepoAsync(configFilePath.ToString(), existingContent);

        SetupChannel(subscriptionToAdd.Channel);

        var options = CreateAddSubscriptionOptions(
            subscriptionToAdd,
            configurationBranch: GetTestBranch());

        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.ErrorCode);

        // Verify the file still only contains the original subscription
        var fullPath = Path.Combine(ConfigurationRepoPath, configFilePath.ToString());
        var subscriptions = await DeserializeSubscriptionsAsync(fullPath);
        subscriptions.Should().HaveCount(1);
        subscriptions[0].Id.Should().Be(Guid.Parse(existingSubscriptionId));
    }

    [Test]
    public async Task AddSubscriptionOperation_CopyFromSubscription_CopiesAllSettings()
    {
        // Arrange - Create a source subscription to copy from
        var sourceSubscriptionId = Guid.NewGuid();
        var sourceChannel = new Channel(42, "test-channel", "test");
        var sourceSubscription = new Subscription(
            id: sourceSubscriptionId,
            enabled: true,
            sourceEnabled: false,
            sourceRepository: "https://github.com/dotnet/source-repo",
            targetRepository: "https://github.com/dotnet/target-repo",
            targetBranch: "main",
            pullRequestFailureNotificationTags: "tag1;tag2",
            sourceDirectory: null,
            targetDirectory: null,
            excludedAssets: new List<string> { "System.Text.Json" })
        {
            Channel = sourceChannel,
            Policy = new SubscriptionPolicy(batchable: false, updateFrequency: UpdateFrequency.EveryDay)
            {
                MergePolicies = new List<MergePolicy>
                {
                    new MergePolicy
                    {
                        Name = MergePolicyConstants.AllCheckSuccessfulMergePolicyName,
                        Properties = []
                    }
                }
            }
        };

        // Setup the mock to return the source subscription
        BarClientMock
            .Setup(x => x.GetSubscriptionAsync(sourceSubscriptionId.ToString()))
            .ReturnsAsync(sourceSubscription);

        SetupChannel(sourceChannel.Name);

        var testBranch = GetTestBranch();
        var options = new AddSubscriptionCommandLineOptions
        {
            CopyFromSubscription = sourceSubscriptionId.ToString(),
            ConfigurationRepository = ConfigurationRepoPath,
            ConfigurationBranch = testBranch,
            ConfigurationBaseBranch = DefaultBranch,
            NoPr = true,
            Quiet = true,
            NoTriggerOnCreate = true,
            IgnoreChecks = []
        };

        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);

        // Verify the new subscription was created with copied settings
        await CheckoutBranch(testBranch);
        var expectedSubscription = new SubscriptionYaml
        {
            Id = Guid.NewGuid(), // Placeholder ID for path calculation
            Channel = sourceChannel.Name,
            SourceRepository = sourceSubscription.SourceRepository,
            TargetRepository = sourceSubscription.TargetRepository,
            TargetBranch = sourceSubscription.TargetBranch
        };
        var expectedFilePath = ConfigFilePathResolver.GetDefaultSubscriptionFilePath(expectedSubscription);
        var fullExpectedPath = Path.Combine(ConfigurationRepoPath, expectedFilePath.ToString());
        File.Exists(fullExpectedPath).Should().BeTrue($"Expected file at {fullExpectedPath}");

        var subscriptions = await DeserializeSubscriptionsAsync(fullExpectedPath);
        subscriptions.Should().HaveCount(1);

        var actualSubscription = subscriptions[0];
        actualSubscription.Channel.Should().Be(sourceSubscription.Channel.Name);
        actualSubscription.SourceRepository.Should().Be(sourceSubscription.SourceRepository);
        actualSubscription.TargetRepository.Should().Be(sourceSubscription.TargetRepository);
        actualSubscription.TargetBranch.Should().Be(sourceSubscription.TargetBranch);
        actualSubscription.UpdateFrequency.Should().Be(sourceSubscription.Policy.UpdateFrequency);
        actualSubscription.Enabled.Should().Be(sourceSubscription.Enabled);
        actualSubscription.FailureNotificationTags.Should().Be(sourceSubscription.PullRequestFailureNotificationTags);
        actualSubscription.ExcludedAssets.Should().BeEquivalentTo(sourceSubscription.ExcludedAssets);
        actualSubscription.MergePolicies.Should().HaveCount(1);
        actualSubscription.MergePolicies[0].Name.Should().Be(MergePolicyConstants.AllCheckSuccessfulMergePolicyName);
    }

    [Test]
    public async Task AddSubscriptionOperation_CopyFromSubscription_OverrideWithCommandLineOptions()
    {
        // Arrange - Create a source subscription to copy from
        var sourceSubscriptionId = Guid.NewGuid();
        var sourceChannel = new Channel(42, "source-channel", "test");
        var targetChannel = new Channel(43, "target-channel", "test");
        var sourceSubscription = new Subscription(
            id: sourceSubscriptionId,
            enabled: true,
            sourceEnabled: false,
            sourceRepository: "https://github.com/dotnet/source-repo",
            targetRepository: "https://github.com/dotnet/original-target",
            targetBranch: "main",
            pullRequestFailureNotificationTags: "tag1",
            sourceDirectory: null,
            targetDirectory: null,
            excludedAssets: new List<string>())
        {
            Channel = sourceChannel,
            Policy = new SubscriptionPolicy(batchable: false, updateFrequency: UpdateFrequency.EveryDay)
            {
                MergePolicies = new List<MergePolicy>()
            }
        };

        // Setup the mocks
        BarClientMock
            .Setup(x => x.GetSubscriptionAsync(sourceSubscriptionId.ToString()))
            .ReturnsAsync(sourceSubscription);

        SetupChannel(sourceChannel.Name, channelId: sourceChannel.Id);
        SetupChannel(targetChannel.Name, channelId: targetChannel.Id);

        var testBranch = GetTestBranch();
        var newTargetRepo = "https://github.com/dotnet/new-target";
        var newTargetBranch = "release/1.0";
        
        var options = new AddSubscriptionCommandLineOptions
        {
            CopyFromSubscription = sourceSubscriptionId.ToString(),
            Channel = targetChannel.Name, // Override channel
            TargetRepository = newTargetRepo, // Override target repo
            TargetBranch = newTargetBranch, // Override target branch
            ConfigurationRepository = ConfigurationRepoPath,
            ConfigurationBranch = testBranch,
            ConfigurationBaseBranch = DefaultBranch,
            NoPr = true,
            Quiet = true,
            NoTriggerOnCreate = true,
            IgnoreChecks = []
        };

        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);

        // Verify the new subscription has overridden values
        await CheckoutBranch(testBranch);
        var expectedSubscription = new SubscriptionYaml
        {
            Id = Guid.NewGuid(), // Placeholder ID for path calculation
            Channel = targetChannel.Name,
            SourceRepository = sourceSubscription.SourceRepository,
            TargetRepository = newTargetRepo,
            TargetBranch = newTargetBranch
        };
        var expectedFilePath = ConfigFilePathResolver.GetDefaultSubscriptionFilePath(expectedSubscription);
        var fullExpectedPath = Path.Combine(ConfigurationRepoPath, expectedFilePath.ToString());
        File.Exists(fullExpectedPath).Should().BeTrue($"Expected file at {fullExpectedPath}");

        var subscriptions = await DeserializeSubscriptionsAsync(fullExpectedPath);
        subscriptions.Should().HaveCount(1);

        var actualSubscription = subscriptions[0];
        actualSubscription.Channel.Should().Be(targetChannel.Name); // Overridden
        actualSubscription.SourceRepository.Should().Be(sourceSubscription.SourceRepository); // Copied
        actualSubscription.TargetRepository.Should().Be(newTargetRepo); // Overridden
        actualSubscription.TargetBranch.Should().Be(newTargetBranch); // Overridden
    }

    private AddSubscriptionCommandLineOptions CreateAddSubscriptionOptions(
        SubscriptionYaml subscription,
        string? configurationBranch = null,
        string configurationBaseBranch = DefaultBranch,
        string? configurationFilePath = null,
        bool noPr = true)
    {
        return new AddSubscriptionCommandLineOptions
        {
            Channel = subscription.Channel,
            SourceRepository = subscription.SourceRepository,
            TargetRepository = subscription.TargetRepository,
            TargetBranch = subscription.TargetBranch,
            UpdateFrequency = subscription.UpdateFrequency.ToString(),
            ConfigurationRepository = ConfigurationRepoPath,
            ConfigurationBranch = configurationBranch,
            ConfigurationBaseBranch = configurationBaseBranch,
            ConfigurationFilePath = configurationFilePath,
            NoPr = noPr,
            Quiet = true,
            NoTriggerOnCreate = true,
            SourceEnabled = subscription.SourceEnabled,
            SourceDirectory = subscription.SourceDirectory,
            TargetDirectory = subscription.TargetDirectory,
            Enabled = subscription.Enabled,
            IgnoreChecks = []
        };
    }

    private AddSubscriptionOperation CreateOperation(AddSubscriptionCommandLineOptions options)
    {
        return new AddSubscriptionOperation(
            options,
            _loggerMock.Object,
            BarClientMock.Object,
            RemoteFactoryMock.Object,
            GitRepoFactory,
            ConfigurationRepositoryManager);
    }
}
