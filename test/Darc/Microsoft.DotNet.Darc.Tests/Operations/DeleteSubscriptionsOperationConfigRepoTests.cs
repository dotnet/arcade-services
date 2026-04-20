// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System;
using System.IO;
using System.Linq;
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
public class DeleteSubscriptionsOperationConfigRepoTests : ConfigurationManagementTestBase
{
    private Mock<ILogger<DeleteSubscriptionsOperation>> _loggerMock = null!;

    [SetUp]
    public override async Task SetupAsync()
    {
        await base.SetupAsync();
        _loggerMock = new Mock<ILogger<DeleteSubscriptionsOperation>>();
    }

    [Test]
    public async Task DeleteSubscriptionOperation_WithConfigRepo_RemovesSubscriptionFromFile()
    {
        // Arrange
        var subscriptionToDeleteId = Guid.NewGuid();
        var subscriptionToKeepId = Guid.NewGuid();
        var subscriptionToDelete = CreateTestSubscription(subscriptionToDeleteId);
        var subscriptionToKeep = CreateTestSubscription(subscriptionToKeepId, sourceRepo: "https://github.com/dotnet/other-source");
        var testBranch = GetTestBranch();

        // Create subscription file with both subscriptions - one to delete and one to keep
        var configFilePath = new UnixPath(ConfigFilePathResolver.SubscriptionFolderPath) / "dotnet-target-repo.yml";
        var existingContent = $"""
            {CreateSubscriptionYamlContent(subscriptionToDelete)}

            {CreateSubscriptionYamlContent(subscriptionToKeep)}
            """;
        await CreateFileInConfigRepoAsync(configFilePath.ToString(), existingContent);

        SetupGetSubscriptionAsync(subscriptionToDelete);

        var options = CreateDeleteSubscriptionsOptions(
            subscriptionId: subscriptionToDeleteId.ToString(),
            configurationBranch: testBranch);
        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);

        // Verify only the remaining subscription is in the file
        await CheckoutBranch(testBranch);
        var fullPath = Path.Combine(ConfigurationRepoPath, configFilePath.ToString());
        File.Exists(fullPath).Should().BeTrue("File should still exist with remaining subscription");

        var subscriptions = await DeserializeSubscriptionsAsync(fullPath);
        subscriptions.Should().HaveCount(1);
        subscriptions[0].Id.Should().Be(subscriptionToKeepId);
        subscriptions[0].SourceRepository.Should().Be(subscriptionToKeep.SourceRepository);
    }

    [Test]
    public async Task DeleteSubscriptionOperation_WithConfigRepo_UsesSpecifiedConfigFilePath()
    {
        // Arrange - when a specific file path is provided, it should be used directly
        var subscriptionToDeleteId = Guid.NewGuid();
        var subscriptionToKeepId = Guid.NewGuid();
        var subscriptionToDelete = CreateTestSubscription(subscriptionToDeleteId);
        var subscriptionToKeep = CreateTestSubscription(subscriptionToKeepId, sourceRepo: "https://github.com/dotnet/other-source");
        var testBranch = GetTestBranch();

        // Create subscription file at a custom path
        var specifiedFilePath = new UnixPath(ConfigFilePathResolver.SubscriptionFolderPath) / "my-custom-file.yml";
        var existingContent = $"""
            {CreateSubscriptionYamlContent(subscriptionToDelete)}

            {CreateSubscriptionYamlContent(subscriptionToKeep)}
            """;
        await CreateFileInConfigRepoAsync(specifiedFilePath.ToString(), existingContent);

        SetupGetSubscriptionAsync(subscriptionToDelete);

        var options = CreateDeleteSubscriptionsOptions(
            subscriptionId: subscriptionToDeleteId.ToString(),
            configurationBranch: testBranch,
            configurationFilePath: specifiedFilePath.ToString());
        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);

        // Verify only the remaining subscription is in the file
        await CheckoutBranch(testBranch);
        var fullPath = Path.Combine(ConfigurationRepoPath, specifiedFilePath.ToString());
        File.Exists(fullPath).Should().BeTrue("File should still exist with remaining subscription");

        var subscriptions = await DeserializeSubscriptionsAsync(fullPath);
        subscriptions.Should().HaveCount(1);
        subscriptions[0].Id.Should().Be(subscriptionToKeepId);
    }

    [Test]
    public async Task DeleteSubscriptionOperation_WithConfigRepo_FindsSubscriptionInNonDefaultFile()
    {
        // Arrange - subscription is in a file that doesn't match the default naming convention
        // so the operation must search through all files in the subscriptions folder
        var subscriptionToDeleteId = Guid.NewGuid();
        var subscriptionToDelete = CreateTestSubscription(subscriptionToDeleteId);
        var testBranch = GetTestBranch();

        // Create two files that DON'T contain the subscription we're looking for
        // This ensures the search has to go through multiple files
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

        // Create the file with ONLY the subscription we want to delete (file should be deleted after)
        var customFilePath = new UnixPath(ConfigFilePathResolver.SubscriptionFolderPath) / "zzz-custom-subscriptions.yml";
        await CreateFileInConfigRepoAsync(customFilePath.ToString(), CreateSubscriptionYamlContent(subscriptionToDelete));

        SetupGetSubscriptionAsync(subscriptionToDelete);

        // Note: we do NOT specify configurationFilePath - the operation should find it by searching
        var options = CreateDeleteSubscriptionsOptions(
            subscriptionId: subscriptionToDeleteId.ToString(),
            configurationBranch: testBranch);
        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);

        // Verify the file was deleted since it had only one subscription
        await CheckoutBranch(testBranch);
        var fullPath = Path.Combine(ConfigurationRepoPath, customFilePath.ToString());
        File.Exists(fullPath).Should().BeFalse("File should be deleted when last subscription is removed");

        // Verify the unrelated files were not modified
        var unrelatedFile1FullPath = Path.Combine(ConfigurationRepoPath, unrelatedFile1Path.ToString());
        var unrelatedFile1Subscriptions = await DeserializeSubscriptionsAsync(unrelatedFile1FullPath);
        unrelatedFile1Subscriptions.Should().HaveCount(1);
        unrelatedFile1Subscriptions[0].SourceRepository.Should().Be(unrelatedSubscription1.SourceRepository);

        var unrelatedFile2FullPath = Path.Combine(ConfigurationRepoPath, unrelatedFile2Path.ToString());
        var unrelatedFile2Subscriptions = await DeserializeSubscriptionsAsync(unrelatedFile2FullPath);
        unrelatedFile2Subscriptions.Should().HaveCount(1);
        unrelatedFile2Subscriptions[0].SourceRepository.Should().Be(unrelatedSubscription2.SourceRepository);
    }

    [Test]
    public async Task DeleteSubscriptionOperation_WithConfigRepo_DeletesMultipleSubscriptionsOnSingleBranch()
    {
        // Arrange
        var subscription1Id = Guid.NewGuid();
        var subscription2Id = Guid.NewGuid();
        var sharedTargetRepo = "https://github.com/dotnet/dotnet";
        var subscription1 = CreateTestSubscription(subscription1Id, targetRepo: sharedTargetRepo, sourceRepo: "https://github.com/dotnet/runtime");
        var subscription2 = CreateTestSubscription(subscription2Id, targetRepo: sharedTargetRepo, sourceRepo: "https://github.com/dotnet/aspnetcore");

        // Create separate config files for each subscription
        var configFilePath1 = new UnixPath(ConfigFilePathResolver.SubscriptionFolderPath) / "dotnet-dotnet-runtime.yml";
        var configFilePath2 = new UnixPath(ConfigFilePathResolver.SubscriptionFolderPath) / "dotnet-dotnet-aspnetcore.yml";
        await CreateFileInConfigRepoAsync(configFilePath1.ToString(), CreateSubscriptionYamlContent(subscription1));
        await CreateFileInConfigRepoAsync(configFilePath2.ToString(), CreateSubscriptionYamlContent(subscription2));

        // Set up BAR client to return both subscriptions when filtering
        BarClientMock
            .Setup(x => x.GetSubscriptionsAsync(null, null, null, null, null, null))
            .ReturnsAsync([subscription1, subscription2]);
        BarClientMock
            .Setup(x => x.GetDefaultChannelsAsync(null, null, null))
            .ReturnsAsync([]);

        // Get branches before operation
        var branchesBefore = await GetBranchesAsync();

        // Use filter options (--target-repo) instead of --id to trigger bulk deletion
        var options = CreateDeleteSubscriptionsOptions(
            targetRepo: sharedTargetRepo);
        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);

        // The key assertion: all deletions should be on a SINGLE new branch
        var branchesAfter = await GetBranchesAsync();
        var newBranches = branchesAfter.Except(branchesBefore).ToList();
        newBranches.Should().HaveCount(1, "all subscription deletions should be batched into a single branch");

        // Verify both subscription files were deleted on that branch
        await CheckoutBranch(newBranches[0]);
        var fullPath1 = Path.Combine(ConfigurationRepoPath, configFilePath1.ToString());
        var fullPath2 = Path.Combine(ConfigurationRepoPath, configFilePath2.ToString());
        File.Exists(fullPath1).Should().BeFalse("first subscription file should be deleted");
        File.Exists(fullPath2).Should().BeFalse("second subscription file should be deleted");
    }

    #region Helper methods

    private Subscription CreateTestSubscription(
        Guid id,
        string channel = "test-channel",
        string sourceRepo = "https://github.com/dotnet/source-repo",
        string targetRepo = "https://github.com/dotnet/target-repo",
        string targetBranch = "main")
    {
        return new Subscription(
            id: id,
            enabled: true,
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
            """;
    }

    private void SetupGetSubscriptionAsync(Subscription subscription)
    {
        BarClientMock
            .Setup(x => x.GetSubscriptionAsync(subscription.Id.ToString()))
            .ReturnsAsync(subscription);
    }

    private DeleteSubscriptionsCommandLineOptions CreateDeleteSubscriptionsOptions(
        string? subscriptionId = null,
        string? targetRepo = null,
        string? sourceRepo = null,
        string? channel = null,
        string? targetBranch = null,
        string? configurationBranch = null,
        string configurationBaseBranch = DefaultBranch,
        string? configurationFilePath = null,
        bool noPr = true,
        bool noConfirmation = true)
    {
        return new DeleteSubscriptionsCommandLineOptions
        {
            Id = subscriptionId ?? string.Empty,
            TargetRepository = targetRepo ?? string.Empty,
            SourceRepository = sourceRepo ?? string.Empty,
            Channel = channel ?? string.Empty,
            TargetBranch = targetBranch ?? string.Empty,
            ConfigurationRepository = ConfigurationRepoPath,
            ConfigurationBranch = configurationBranch,
            ConfigurationBaseBranch = configurationBaseBranch,
            ConfigurationFilePath = configurationFilePath,
            NoPr = noPr,
            NoConfirmation = noConfirmation
        };
    }

    private DeleteSubscriptionsOperation CreateOperation(DeleteSubscriptionsCommandLineOptions options)
    {
        return new DeleteSubscriptionsOperation(
            options,
            BarClientMock.Object,
            ConfigurationRepositoryManager,
            _loggerMock.Object);
    }

    #endregion
}
