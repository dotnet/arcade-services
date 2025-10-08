// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DotNet.Darc;
using Microsoft.DotNet.Darc.Models.PopUps;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.Darc.Tests.Models;

[TestFixture]
public class UpdateSubscriptionPopUpTests
{
    private Mock<ILogger> _mockLogger;
    private Mock<IGitRepoFactory> _mockGitRepoFactory;

    [SetUp]
    public void Setup()
    {
        _mockLogger = new Mock<ILogger>();
        _mockGitRepoFactory = new Mock<IGitRepoFactory>();
    }

    [Test]
    public async Task ParseAndValidateData_WhenIdChanged_ShouldReturnError()
    {
        // Arrange
        var originalSubscription = CreateTestSubscription();
        var popup = CreatePopUp(originalSubscription);
        
        var data = CreateSubscriptionUpdateData(originalSubscription);
        data.Id = "different-id";

        // Act
        var result = await InvokeParseAndValidateData(popup, data);

        // Assert
        result.Should().Be(Constants.ErrorCode);
        _mockLogger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("immutable fields")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.AtLeastOnce);
    }

    [Test]
    public async Task ParseAndValidateData_WhenTargetRepositoryChanged_ShouldReturnError()
    {
        // Arrange
        var originalSubscription = CreateTestSubscription();
        var popup = CreatePopUp(originalSubscription);
        
        var data = CreateSubscriptionUpdateData(originalSubscription);
        data.TargetRepository = "https://github.com/different/repo";

        // Act
        var result = await InvokeParseAndValidateData(popup, data);

        // Assert
        result.Should().Be(Constants.ErrorCode);
        _mockLogger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Target Repository URL")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.AtLeastOnce);
    }

    [Test]
    public async Task ParseAndValidateData_WhenTargetBranchChanged_ShouldReturnError()
    {
        // Arrange
        var originalSubscription = CreateTestSubscription();
        var popup = CreatePopUp(originalSubscription);
        
        var data = CreateSubscriptionUpdateData(originalSubscription);
        data.TargetBranch = "different-branch";

        // Act
        var result = await InvokeParseAndValidateData(popup, data);

        // Assert
        result.Should().Be(Constants.ErrorCode);
        _mockLogger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Target Branch")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.AtLeastOnce);
    }

    [Test]
    public async Task ParseAndValidateData_WhenSourceEnabledChanged_ShouldReturnError()
    {
        // Arrange
        var originalSubscription = CreateTestSubscription();
        // Use forceCreation to skip VMR validation
        var popup = new UpdateSubscriptionPopUp(
            path: "test-path",
            forceCreation: true, // Skip VMR validation
            gitRepoFactory: _mockGitRepoFactory.Object,
            logger: _mockLogger.Object,
            subscription: originalSubscription,
            suggestedChannels: new[] { "Test Channel" },
            suggestedRepositories: new[] { originalSubscription.SourceRepository, originalSubscription.TargetRepository },
            availableUpdateFrequencies: new[] { "EveryDay", "EveryBuild" },
            availableMergePolicyHelp: new[] { "Test" },
            failureNotificationTags: "",
            sourceEnabled: false,
            sourceDirectory: null,
            targetDirectory: null,
            excludedAssets: new List<string>());
        
        var data = CreateSubscriptionUpdateData(originalSubscription);
        data.SourceEnabled = "True"; // Original is False
        data.SourceDirectory = "test-dir"; // Add directory to pass base validation

        // Act
        var result = await InvokeParseAndValidateData(popup, data);

        // Assert
        result.Should().Be(Constants.ErrorCode);
        _mockLogger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Source Enabled")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.AtLeastOnce);
    }

    [Test]
    public async Task ParseAndValidateData_WhenMultipleImmutableFieldsChanged_ShouldReturnErrorWithAllFields()
    {
        // Arrange
        var originalSubscription = CreateTestSubscription();
        var popup = CreatePopUp(originalSubscription);
        
        var data = CreateSubscriptionUpdateData(originalSubscription);
        data.Id = "different-id";
        data.TargetBranch = "different-branch";

        // Act
        var result = await InvokeParseAndValidateData(popup, data);

        // Assert
        result.Should().Be(Constants.ErrorCode);
        _mockLogger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Id")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.AtLeastOnce);
        _mockLogger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Target Branch")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.AtLeastOnce);
    }

    [Test]
    public async Task ParseAndValidateData_WhenNoImmutableFieldsChanged_ShouldSucceed()
    {
        // Arrange
        var originalSubscription = CreateTestSubscription();
        var popup = CreatePopUp(originalSubscription);
        
        var data = CreateSubscriptionUpdateData(originalSubscription);
        // Change only mutable fields
        data.Channel = "Different Channel";
        data.UpdateFrequency = "EveryBuild";

        // Act
        var result = await InvokeParseAndValidateData(popup, data);

        // Assert
        result.Should().Be(Constants.SuccessCode);
    }

    private Subscription CreateTestSubscription()
    {
        var subscription = new Subscription(
            id: new Guid("12345678-1234-1234-1234-123456789012"),
            enabled: true,
            sourceEnabled: false,
            sourceRepository: "https://github.com/test/source",
            targetRepository: "https://github.com/test/target",
            targetBranch: "main",
            sourceDirectory: null,
            targetDirectory: null,
            pullRequestFailureNotificationTags: "",
            excludedAssets: new List<string>());
        
        subscription.Channel = new Channel(1, "Test Channel", "test");
        subscription.Policy = new SubscriptionPolicy(
            batchable: false,
            updateFrequency: UpdateFrequency.EveryDay)
        {
            MergePolicies = new List<MergePolicy>()
        };
        
        return subscription;
    }

    private UpdateSubscriptionPopUp CreatePopUp(Subscription subscription)
    {
        return new UpdateSubscriptionPopUp(
            path: "test-path",
            forceCreation: false,
            gitRepoFactory: _mockGitRepoFactory.Object,
            logger: _mockLogger.Object,
            subscription: subscription,
            suggestedChannels: new[] { "Test Channel" },
            suggestedRepositories: new[] { subscription.SourceRepository, subscription.TargetRepository },
            availableUpdateFrequencies: new[] { "EveryDay", "EveryBuild" },
            availableMergePolicyHelp: new[] { "Test" },
            failureNotificationTags: "",
            sourceEnabled: false,
            sourceDirectory: null,
            targetDirectory: null,
            excludedAssets: new List<string>());
    }

    private SubscriptionUpdateData CreateSubscriptionUpdateData(Subscription subscription)
    {
        return new SubscriptionUpdateData
        {
            Id = subscription.Id.ToString(),
            Channel = subscription.Channel.Name,
            SourceRepository = subscription.SourceRepository,
            TargetRepository = subscription.TargetRepository,
            TargetBranch = subscription.TargetBranch,
            UpdateFrequency = subscription.Policy.UpdateFrequency.ToString(),
            Batchable = subscription.Policy.Batchable.ToString(),
            Enabled = subscription.Enabled.ToString(),
            FailureNotificationTags = subscription.PullRequestFailureNotificationTags,
            SourceEnabled = subscription.SourceEnabled.ToString(),
            SourceDirectory = subscription.SourceDirectory,
            TargetDirectory = subscription.TargetDirectory,
            MergePolicies = new List<MergePolicyData>(),
            ExcludedAssets = new List<string>()
        };
    }

    private async Task<int> InvokeParseAndValidateData(UpdateSubscriptionPopUp popup, SubscriptionUpdateData data)
    {
        // We need to use reflection to call the protected method
        var method = typeof(UpdateSubscriptionPopUp).GetMethod(
            "ParseAndValidateData",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        var task = (Task<int>)method.Invoke(popup, new object[] { data });
        return await task;
    }
}
