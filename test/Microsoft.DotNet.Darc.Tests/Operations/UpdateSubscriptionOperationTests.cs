// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.Darc.Tests.Operations;

[TestFixture]
public class UpdateSubscriptionOperationTests
{
    [Test]
    public async Task UpdateToBatchedCodeflowSubscriptionFails()
    {
        // Arrange
        var options = new UpdateSubscriptionCommandLineOptions
        {
            Id = "12345678-1234-1234-1234-123456789012",
            Batchable = true,
            SourceEnabled = true
        };

        var mockLogger = new Mock<ILogger<UpdateSubscriptionOperation>>();
        var mockBarClient = new Mock<IBarApiClient>();
        var mockGitRepoFactory = new Mock<IGitRepoFactory>();

        // Mock the existing subscription (non-batched codeflow)
        var existingSubscription = new Microsoft.DotNet.ProductConstructionService.Client.Models.Subscription()
        {
            Id = Guid.Parse(options.Id),
            Channel = new Microsoft.DotNet.ProductConstructionService.Client.Models.Channel() { Name = "test-channel" },
            SourceRepository = "https://github.com/dotnet/test-source",
            Policy = new Microsoft.DotNet.ProductConstructionService.Client.Models.SubscriptionPolicy()
            {
                Batchable = false,
                UpdateFrequency = Microsoft.DotNet.ProductConstructionService.Client.Models.UpdateFrequency.EveryBuild,
                MergePolicies = new List<Microsoft.DotNet.ProductConstructionService.Client.Models.MergePolicy>()
            },
            Enabled = true,
            SourceEnabled = true,
            ExcludedAssets = new List<string>()
        };

        mockBarClient.Setup(b => b.GetSubscriptionAsync(It.IsAny<Guid>()))
            .ReturnsAsync(existingSubscription);

        var operation = new UpdateSubscriptionOperation(
            options,
            mockBarClient.Object,
            mockGitRepoFactory.Object,
            mockLogger.Object);

        // Act
        var result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.ErrorCode);
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Batched codeflow subscriptions are not supported")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Test]
    public async Task UpdateToBatchedNonCodeflowSubscriptionSucceeds()
    {
        // Arrange
        var options = new UpdateSubscriptionCommandLineOptions
        {
            Id = "12345678-1234-1234-1234-123456789012",
            Batchable = true,
            SourceEnabled = false // Not a codeflow subscription
        };

        var mockLogger = new Mock<ILogger<UpdateSubscriptionOperation>>();
        var mockBarClient = new Mock<IBarApiClient>();
        var mockGitRepoFactory = new Mock<IGitRepoFactory>();

        // Mock the existing subscription
        var existingSubscription = new Microsoft.DotNet.ProductConstructionService.Client.Models.Subscription()
        {
            Id = Guid.Parse(options.Id),
            Channel = new Microsoft.DotNet.ProductConstructionService.Client.Models.Channel() { Name = "test-channel" },
            SourceRepository = "https://github.com/dotnet/test-source",
            Policy = new Microsoft.DotNet.ProductConstructionService.Client.Models.SubscriptionPolicy()
            {
                Batchable = false,
                UpdateFrequency = Microsoft.DotNet.ProductConstructionService.Client.Models.UpdateFrequency.EveryBuild,
                MergePolicies = new List<Microsoft.DotNet.ProductConstructionService.Client.Models.MergePolicy>()
            },
            Enabled = true,
            SourceEnabled = false,
            ExcludedAssets = new List<string>()
        };

        mockBarClient.Setup(b => b.GetSubscriptionAsync(It.IsAny<Guid>()))
            .ReturnsAsync(existingSubscription);

        // Mock the update call
        var updatedSubscription = existingSubscription;
        updatedSubscription.Policy.Batchable = true;
        mockBarClient.Setup(b => b.UpdateSubscriptionAsync(
                It.IsAny<Guid>(),
                It.IsAny<Microsoft.DotNet.ProductConstructionService.Client.Models.SubscriptionUpdate>()))
            .ReturnsAsync(updatedSubscription);

        var operation = new UpdateSubscriptionOperation(
            options,
            mockBarClient.Object,
            mockGitRepoFactory.Object,
            mockLogger.Object);

        // Act
        var result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);
        
        // Verify that no error about batched codeflow was logged
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Batched codeflow subscriptions are not supported")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }
}