// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System;
using System.IO;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.MaestroConfiguration.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.Darc.Tests.Operations;

[TestFixture]
public class UpdateSubscriptionOperationConfigRepoTests : ConfigurationManagementTestBase
{
    private Mock<ILogger<UpdateSubscriptionOperation>> _loggerMock = null!;
    private Mock<DarcLib.IGitRepoFactory> _gitRepoFactoryMock = null!;

    [SetUp]
    public override async Task SetupAsync()
    {
        await base.SetupAsync();
        _loggerMock = new Mock<ILogger<UpdateSubscriptionOperation>>();
        _gitRepoFactoryMock = new Mock<DarcLib.IGitRepoFactory>();
    }

    [Test]
    public async Task UpdateSubscriptionOperation_WithConfigRepo_UpdatesSubscriptionInFile()
    {
        // Arrange
        var subscriptionId = Guid.NewGuid();
        var otherSubscriptionId = Guid.NewGuid();
        var originalSubscription = CreateTestSubscription(subscriptionId, channel: "original-channel");
        var otherSubscription = CreateTestSubscription(otherSubscriptionId, sourceRepo: "https://github.com/dotnet/other-source");
        var testBranch = GetTestBranch();

        // Create subscription file with both subscriptions
        var configFilePath = new UnixPath(ConfigFilePathResolver.SubscriptionFolderPath) / "dotnet-target-repo.yml";
        var existingContent = $"""
            {CreateSubscriptionYamlContent(originalSubscription)}

            {CreateSubscriptionYamlContent(otherSubscription)}
            """;
        await CreateFileInConfigRepoAsync(configFilePath.ToString(), existingContent);

        SetupGetSubscriptionAsync(originalSubscription);
        SetupChannel("updated-channel");

        var options = CreateUpdateSubscriptionOptions(
            subscriptionId: subscriptionId.ToString(),
            channel: "updated-channel",
            configurationBranch: testBranch);
        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);

        // Verify the subscription was updated
        await CheckoutBranch(testBranch);
        var fullPath = Path.Combine(ConfigurationRepoPath, configFilePath.ToString());
        File.Exists(fullPath).Should().BeTrue("File should still exist");

        var subscriptions = await DeserializeSubscriptionsAsync(fullPath);
        subscriptions.Should().HaveCount(2);

        var updatedSubscription = subscriptions.Find(s => s.Id == subscriptionId);
        updatedSubscription.Should().NotBeNull();
        updatedSubscription!.Channel.Should().Be("updated-channel");
        updatedSubscription.SourceRepository.Should().Be(originalSubscription.SourceRepository);
        updatedSubscription.TargetRepository.Should().Be(originalSubscription.TargetRepository);
        updatedSubscription.TargetBranch.Should().Be(originalSubscription.TargetBranch);

        // Verify the other subscription was not modified
        var unchangedSubscription = subscriptions.Find(s => s.Id == otherSubscriptionId);
        unchangedSubscription.Should().NotBeNull();
        unchangedSubscription!.Channel.Should().Be(otherSubscription.Channel.Name);
    }

    [Test]
    public async Task UpdateSubscriptionOperation_WithConfigRepo_UsesSpecifiedConfigFilePath()
    {
        // Arrange
        var subscriptionId = Guid.NewGuid();
        var originalSubscription = CreateTestSubscription(subscriptionId, channel: "original-channel");
        var testBranch = GetTestBranch();

        // Create subscription file at a custom path
        var specifiedFilePath = new UnixPath(ConfigFilePathResolver.SubscriptionFolderPath) / "my-custom-file.yml";
        await CreateFileInConfigRepoAsync(specifiedFilePath.ToString(), CreateSubscriptionYamlContent(originalSubscription));

        SetupGetSubscriptionAsync(originalSubscription);
        SetupChannel("updated-channel");

        var options = CreateUpdateSubscriptionOptions(
            subscriptionId: subscriptionId.ToString(),
            channel: "updated-channel",
            configurationBranch: testBranch,
            configurationFilePath: specifiedFilePath.ToString());
        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);

        // Verify the subscription was updated
        await CheckoutBranch(testBranch);
        var fullPath = Path.Combine(ConfigurationRepoPath, specifiedFilePath.ToString());
        File.Exists(fullPath).Should().BeTrue("File should still exist");

        var subscriptions = await DeserializeSubscriptionsAsync(fullPath);
        subscriptions.Should().HaveCount(1);
        subscriptions[0].Id.Should().Be(subscriptionId);
        subscriptions[0].Channel.Should().Be("updated-channel");
    }

    [Test]
    public async Task UpdateSubscriptionOperation_WithConfigRepo_FindsSubscriptionInNonDefaultFile()
    {
        // Arrange - subscription is in a file that doesn't match the default naming convention
        var subscriptionId = Guid.NewGuid();
        var originalSubscription = CreateTestSubscription(subscriptionId, channel: "original-channel");
        var testBranch = GetTestBranch();

        // Create two files that DON'T contain the subscription we're looking for
        var unrelatedFile1Path = new UnixPath(ConfigFilePathResolver.SubscriptionFolderPath) / "aaa-first-file.yml";
        var unrelatedSubscription1 = CreateTestSubscription(
            Guid.NewGuid(),
            sourceRepo: "https://github.com/dotnet/unrelated-source-1",
            targetRepo: "https://github.com/dotnet/unrelated-target-1");
        await CreateFileInConfigRepoAsync(unrelatedFile1Path.ToString(), CreateSubscriptionYamlContent(unrelatedSubscription1));

        var unrelatedFile2Path = new UnixPath(ConfigFilePathResolver.SubscriptionFolderPath) / "bbb-second-file.yml";
        var unrelatedSubscription2 = CreateTestSubscription(
            Guid.NewGuid(),
            sourceRepo: "https://github.com/dotnet/unrelated-source-2",
            targetRepo: "https://github.com/dotnet/unrelated-target-2");
        await CreateFileInConfigRepoAsync(unrelatedFile2Path.ToString(), CreateSubscriptionYamlContent(unrelatedSubscription2));

        // Create the file with the subscription we want to update
        var customFilePath = new UnixPath(ConfigFilePathResolver.SubscriptionFolderPath) / "zzz-custom-subscriptions.yml";
        await CreateFileInConfigRepoAsync(customFilePath.ToString(), CreateSubscriptionYamlContent(originalSubscription));

        SetupGetSubscriptionAsync(originalSubscription);
        SetupChannel("updated-channel");

        // Note: we do NOT specify configurationFilePath - the operation should find it by searching
        var options = CreateUpdateSubscriptionOptions(
            subscriptionId: subscriptionId.ToString(),
            channel: "updated-channel",
            configurationBranch: testBranch);
        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);

        // Verify the subscription was updated in the correct file
        await CheckoutBranch(testBranch);
        var fullPath = Path.Combine(ConfigurationRepoPath, customFilePath.ToString());
        File.Exists(fullPath).Should().BeTrue("File should still exist");

        var subscriptions = await DeserializeSubscriptionsAsync(fullPath);
        subscriptions.Should().HaveCount(1);
        subscriptions[0].Id.Should().Be(subscriptionId);
        subscriptions[0].Channel.Should().Be("updated-channel");

        // Verify the unrelated files were not modified
        var unrelatedFile1FullPath = Path.Combine(ConfigurationRepoPath, unrelatedFile1Path.ToString());
        var unrelatedFile1Subscriptions = await DeserializeSubscriptionsAsync(unrelatedFile1FullPath);
        unrelatedFile1Subscriptions.Should().HaveCount(1);
        unrelatedFile1Subscriptions[0].Channel.Should().Be(unrelatedSubscription1.Channel.Name);

        var unrelatedFile2FullPath = Path.Combine(ConfigurationRepoPath, unrelatedFile2Path.ToString());
        var unrelatedFile2Subscriptions = await DeserializeSubscriptionsAsync(unrelatedFile2FullPath);
        unrelatedFile2Subscriptions.Should().HaveCount(1);
        unrelatedFile2Subscriptions[0].Channel.Should().Be(unrelatedSubscription2.Channel.Name);
    }

    [Test]
    public async Task UpdateSubscriptionOperation_WithConfigRepo_UpdatesMultipleFields()
    {
        // Arrange
        var subscriptionId = Guid.NewGuid();
        var originalSubscription = CreateTestSubscription(
            subscriptionId,
            channel: "original-channel",
            sourceRepo: "https://github.com/dotnet/original-source");
        var testBranch = GetTestBranch();

        var configFilePath = new UnixPath(ConfigFilePathResolver.SubscriptionFolderPath) / "dotnet-target-repo.yml";
        await CreateFileInConfigRepoAsync(configFilePath.ToString(), CreateSubscriptionYamlContent(originalSubscription));

        SetupGetSubscriptionAsync(originalSubscription);
        SetupChannel("updated-channel");

        var options = CreateUpdateSubscriptionOptions(
            subscriptionId: subscriptionId.ToString(),
            channel: "updated-channel",
            sourceRepoUrl: "https://github.com/dotnet/updated-source",
            updateFrequency: "everyBuild",
            enabled: false,
            configurationBranch: testBranch);
        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);

        await CheckoutBranch(testBranch);
        var fullPath = Path.Combine(ConfigurationRepoPath, configFilePath.ToString());
        var subscriptions = await DeserializeSubscriptionsAsync(fullPath);
        subscriptions.Should().HaveCount(1);

        var updatedSubscription = subscriptions[0];
        updatedSubscription.Id.Should().Be(subscriptionId);
        updatedSubscription.Channel.Should().Be("updated-channel");
        updatedSubscription.SourceRepository.Should().Be("https://github.com/dotnet/updated-source");
        updatedSubscription.UpdateFrequency.Should().Be(UpdateFrequency.EveryBuild);
        updatedSubscription.Enabled.Should().BeFalse();

        // Immutable fields should remain unchanged
        updatedSubscription.TargetRepository.Should().Be(originalSubscription.TargetRepository);
        updatedSubscription.TargetBranch.Should().Be(originalSubscription.TargetBranch);
    }

    #region Helper methods

    private Subscription CreateTestSubscription(
        Guid id,
        string channel = "test-channel",
        string sourceRepo = "https://github.com/dotnet/source-repo",
        string targetRepo = "https://github.com/dotnet/target-repo",
        string targetBranch = "main",
        bool enabled = true)
    {
        return new Subscription(
            id: id,
            enabled: enabled,
            sourceEnabled: false,
            sourceRepository: sourceRepo,
            targetRepository: targetRepo,
            targetBranch: targetBranch,
            pullRequestFailureNotificationTags: null,
            sourceDirectory: null,
            targetDirectory: null,
            excludedAssets: [])
        {
            Channel = new Channel(1, channel, "test"),
            Policy = new SubscriptionPolicy(false, UpdateFrequency.EveryDay)
            {
                MergePolicies = []
            }
        };
    }

    private static string CreateSubscriptionYamlContent(Subscription subscription)
    {
        return $"""
            - Id: {subscription.Id}
              Channel: {subscription.Channel.Name}
              Source Repository URL: {subscription.SourceRepository}
              Target Repository URL: {subscription.TargetRepository}
              Target Branch: {subscription.TargetBranch}
              Update Frequency: {subscription.Policy.UpdateFrequency}
              Enabled: {subscription.Enabled.ToString().ToLower()}
            """;
    }

    private void SetupGetSubscriptionAsync(Subscription subscription)
    {
        BarClientMock
            .Setup(x => x.GetSubscriptionAsync(subscription.Id.ToString()))
            .ReturnsAsync(subscription);
    }

    private UpdateSubscriptionCommandLineOptions CreateUpdateSubscriptionOptions(
        string subscriptionId,
        string? channel = null,
        string? sourceRepoUrl = null,
        string? updateFrequency = null,
        bool? enabled = null,
        bool? batchable = null,
        string? configurationBranch = null,
        string configurationBaseBranch = DefaultBranch,
        string? configurationFilePath = null,
        bool noPr = true)
    {
        return new UpdateSubscriptionCommandLineOptions
        {
            Id = subscriptionId,
            Channel = channel,
            SourceRepoUrl = sourceRepoUrl,
            UpdateFrequency = updateFrequency,
            Enabled = enabled,
            Batchable = batchable,
            IgnoreChecks = [],
            ConfigurationRepository = ConfigurationRepoPath,
            ConfigurationBranch = configurationBranch,
            ConfigurationBaseBranch = configurationBaseBranch,
            ConfigurationFilePath = configurationFilePath,
            NoPr = noPr
        };
    }

    private UpdateSubscriptionOperation CreateOperation(UpdateSubscriptionCommandLineOptions options)
    {
        return new UpdateSubscriptionOperation(
            options,
            BarClientMock.Object,
            _gitRepoFactoryMock.Object,
            ConfigurationRepositoryManager,
            _loggerMock.Object);
    }

    #endregion
}
