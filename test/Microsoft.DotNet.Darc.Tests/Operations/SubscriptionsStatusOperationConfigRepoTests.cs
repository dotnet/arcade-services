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
public class SubscriptionsStatusOperationConfigRepoTests : ConfigurationManagementTestBase
{
    private Mock<ILogger<SubscriptionsStatusOperation>> _loggerMock = null!;

    [SetUp]
    public override async Task SetupAsync()
    {
        await base.SetupAsync();
        _loggerMock = new Mock<ILogger<SubscriptionsStatusOperation>>();
    }

    [Test]
    public async Task SubscriptionsStatusOperation_WithConfigRepo_DisablesSubscription()
    {
        // Arrange
        var subscriptionId = Guid.NewGuid();
        var subscription = CreateTestSubscription(subscriptionId, enabled: true);
        var testBranch = GetTestBranch();

        // Create subscription file with the enabled subscription
        var configFilePath = new UnixPath(ConfigFilePathResolver.SubscriptionFolderPath) / "dotnet-target-repo.yml";
        var existingContent = CreateSubscriptionYamlContent(subscription);
        await CreateFileInConfigRepoAsync(configFilePath.ToString(), existingContent);

        SetupGetSubscriptionAsync(subscription);

        var options = CreateSubscriptionsStatusOptions(
            subscriptionId: subscriptionId.ToString(),
            disable: true,
            configurationBranch: testBranch);
        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);

        // Verify the subscription was disabled
        await CheckoutBranch(testBranch);
        var fullPath = Path.Combine(ConfigurationRepoPath, configFilePath.ToString());
        File.Exists(fullPath).Should().BeTrue("File should still exist");

        var subscriptions = await DeserializeSubscriptionsAsync(fullPath);
        subscriptions.Should().HaveCount(1);
        subscriptions[0].Id.Should().Be(subscriptionId);
        subscriptions[0].Enabled.Should().BeFalse();
    }

    [Test]
    public async Task SubscriptionsStatusOperation_WithConfigRepo_EnablesSubscription()
    {
        // Arrange
        var subscriptionId = Guid.NewGuid();
        var subscription = CreateTestSubscription(subscriptionId, enabled: false);
        var testBranch = GetTestBranch();

        // Create subscription file with the disabled subscription
        var configFilePath = new UnixPath(ConfigFilePathResolver.SubscriptionFolderPath) / "dotnet-target-repo.yml";
        var existingContent = CreateSubscriptionYamlContent(subscription);
        await CreateFileInConfigRepoAsync(configFilePath.ToString(), existingContent);

        SetupGetSubscriptionAsync(subscription);

        var options = CreateSubscriptionsStatusOptions(
            subscriptionId: subscriptionId.ToString(),
            enable: true,
            configurationBranch: testBranch);
        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);

        // Verify the subscription was enabled
        await CheckoutBranch(testBranch);
        var fullPath = Path.Combine(ConfigurationRepoPath, configFilePath.ToString());
        File.Exists(fullPath).Should().BeTrue("File should still exist");

        var subscriptions = await DeserializeSubscriptionsAsync(fullPath);
        subscriptions.Should().HaveCount(1);
        subscriptions[0].Id.Should().Be(subscriptionId);
        subscriptions[0].Enabled.Should().BeTrue();
    }

    [Test]
    public async Task SubscriptionsStatusOperation_WithConfigRepo_PreservesOtherSubscriptionProperties()
    {
        // Arrange
        var subscriptionId = Guid.NewGuid();
        var subscription = CreateTestSubscription(
            subscriptionId,
            enabled: true,
            channel: "test-channel",
            sourceRepo: "https://github.com/dotnet/source-repo",
            targetRepo: "https://github.com/dotnet/target-repo",
            targetBranch: "main");
        var testBranch = GetTestBranch();

        // Create subscription file
        var configFilePath = new UnixPath(ConfigFilePathResolver.SubscriptionFolderPath) / "dotnet-target-repo.yml";
        var existingContent = CreateSubscriptionYamlContent(subscription);
        await CreateFileInConfigRepoAsync(configFilePath.ToString(), existingContent);

        SetupGetSubscriptionAsync(subscription);

        var options = CreateSubscriptionsStatusOptions(
            subscriptionId: subscriptionId.ToString(),
            disable: true,
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
        updatedSubscription.Enabled.Should().BeFalse();
        // All other properties should remain unchanged
        updatedSubscription.Channel.Should().Be(subscription.Channel.Name);
        updatedSubscription.SourceRepository.Should().Be(subscription.SourceRepository);
        updatedSubscription.TargetRepository.Should().Be(subscription.TargetRepository);
        updatedSubscription.TargetBranch.Should().Be(subscription.TargetBranch);
        updatedSubscription.UpdateFrequency.Should().Be(subscription.Policy.UpdateFrequency);
    }

    [Test]
    public async Task SubscriptionsStatusOperation_WithConfigRepo_DoesNotChangeOtherSubscriptions()
    {
        // Arrange
        var subscriptionToChangeId = Guid.NewGuid();
        var subscriptionToKeepId = Guid.NewGuid();
        var subscriptionToChange = CreateTestSubscription(subscriptionToChangeId, enabled: true);
        var subscriptionToKeep = CreateTestSubscription(
            subscriptionToKeepId,
            enabled: true,
            sourceRepo: "https://github.com/dotnet/other-source");
        var testBranch = GetTestBranch();

        // Create subscription file with both subscriptions
        var configFilePath = new UnixPath(ConfigFilePathResolver.SubscriptionFolderPath) / "dotnet-target-repo.yml";
        var existingContent = $"""
            {CreateSubscriptionYamlContent(subscriptionToChange)}

            {CreateSubscriptionYamlContent(subscriptionToKeep)}
            """;
        await CreateFileInConfigRepoAsync(configFilePath.ToString(), existingContent);

        SetupGetSubscriptionAsync(subscriptionToChange);

        var options = CreateSubscriptionsStatusOptions(
            subscriptionId: subscriptionToChangeId.ToString(),
            disable: true,
            configurationBranch: testBranch);
        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);

        await CheckoutBranch(testBranch);
        var fullPath = Path.Combine(ConfigurationRepoPath, configFilePath.ToString());
        var subscriptions = await DeserializeSubscriptionsAsync(fullPath);
        subscriptions.Should().HaveCount(2);

        var changedSubscription = subscriptions.Find(s => s.Id == subscriptionToChangeId);
        changedSubscription.Should().NotBeNull();
        changedSubscription!.Enabled.Should().BeFalse();

        var unchangedSubscription = subscriptions.Find(s => s.Id == subscriptionToKeepId);
        unchangedSubscription.Should().NotBeNull();
        unchangedSubscription!.Enabled.Should().BeTrue(); // Should remain enabled
        unchangedSubscription.SourceRepository.Should().Be(subscriptionToKeep.SourceRepository);
    }

    [Test]
    public async Task SubscriptionsStatusOperation_WithConfigRepo_SkipsAlreadyDisabledSubscription()
    {
        // Arrange
        var subscriptionId = Guid.NewGuid();
        var subscription = CreateTestSubscription(subscriptionId, enabled: false); // Already disabled
        var testBranch = GetTestBranch();

        SetupGetSubscriptionAsync(subscription);

        var options = CreateSubscriptionsStatusOptions(
            subscriptionId: subscriptionId.ToString(),
            disable: true, // Trying to disable an already disabled subscription
            configurationBranch: testBranch);
        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        // Should succeed because all subscriptions are already in the desired state
        result.Should().Be(Constants.SuccessCode);
    }

    [Test]
    public async Task SubscriptionsStatusOperation_WithConfigRepo_SkipsAlreadyEnabledSubscription()
    {
        // Arrange
        var subscriptionId = Guid.NewGuid();
        var subscription = CreateTestSubscription(subscriptionId, enabled: true); // Already enabled
        var testBranch = GetTestBranch();

        SetupGetSubscriptionAsync(subscription);

        var options = CreateSubscriptionsStatusOptions(
            subscriptionId: subscriptionId.ToString(),
            enable: true, // Trying to enable an already enabled subscription
            configurationBranch: testBranch);
        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        // Should succeed because all subscriptions are already in the desired state
        result.Should().Be(Constants.SuccessCode);
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

    private SubscriptionsStatusCommandLineOptions CreateSubscriptionsStatusOptions(
        string? subscriptionId = null,
        bool enable = false,
        bool disable = false,
        string? configurationBranch = null,
        string configurationBaseBranch = DefaultBranch,
        string? configurationFilePath = null,
        bool noPr = true,
        bool noConfirmation = true)
    {
        return new SubscriptionsStatusCommandLineOptions
        {
            Id = subscriptionId ?? string.Empty,
            Enable = enable,
            Disable = disable,
            ConfigurationRepository = ConfigurationRepoPath,
            ConfigurationBranch = configurationBranch,
            ConfigurationBaseBranch = configurationBaseBranch,
            ConfigurationFilePath = configurationFilePath,
            NoPr = noPr,
            NoConfirmation = noConfirmation
        };
    }

    private SubscriptionsStatusOperation CreateOperation(SubscriptionsStatusCommandLineOptions options)
    {
        return new SubscriptionsStatusOperation(
            options,
            BarClientMock.Object,
            ConfigurationRepositoryManager,
            _loggerMock.Object);
    }

    #endregion
}
