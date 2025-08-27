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
public class AddSubscriptionOperationTests
{
    [Test]
    public async Task BatchedCodeflowSubscriptionFails()
    {
        // Arrange
        var options = new AddSubscriptionCommandLineOptions
        {
            Channel = "test-channel",
            SourceRepository = "https://github.com/dotnet/test-source",
            TargetRepository = "https://github.com/dotnet/test-target",
            TargetBranch = "main",
            UpdateFrequency = "everyBuild",
            Batchable = true,
            SourceEnabled = true,
            Quiet = true
        };

        var mockLogger = new Mock<ILogger<AddSubscriptionOperation>>();
        var mockBarClient = new Mock<IBarApiClient>();
        var mockRemoteFactory = new Mock<IRemoteFactory>();
        var mockGitRepoFactory = new Mock<IGitRepoFactory>();

        var operation = new AddSubscriptionOperation(
            options,
            mockLogger.Object,
            mockBarClient.Object,
            mockRemoteFactory.Object,
            mockGitRepoFactory.Object);

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
    public async Task BatchedNonCodeflowSubscriptionSucceeds()
    {
        // Arrange
        var options = new AddSubscriptionCommandLineOptions
        {
            Channel = "test-channel",
            SourceRepository = "https://github.com/dotnet/test-source",
            TargetRepository = "https://github.com/dotnet/test-target",
            TargetBranch = "main",
            UpdateFrequency = "everyBuild",
            Batchable = true,
            SourceEnabled = false, // Not a codeflow subscription
            Quiet = true
        };

        var mockLogger = new Mock<ILogger<AddSubscriptionOperation>>();
        var mockBarClient = new Mock<IBarApiClient>();
        var mockRemoteFactory = new Mock<IRemoteFactory>();
        var mockGitRepoFactory = new Mock<IGitRepoFactory>();

        // Mock the remote verification
        var mockRemote = new Mock<IRemote>();
        mockRemote.Setup(r => r.CheckIfBranchExistsAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        mockRemote.Setup(r => r.CheckIfRepositoryExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(true);
        
        mockRemoteFactory.Setup(f => f.CreateRemoteAsync(It.IsAny<string>()))
            .ReturnsAsync(mockRemote.Object);

        // Mock subscription creation
        var mockSubscription = new Microsoft.DotNet.ProductConstructionService.Client.Models.Subscription()
        {
            Id = Guid.NewGuid()
        };
        mockBarClient.Setup(b => b.CreateSubscriptionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<List<Microsoft.DotNet.ProductConstructionService.Client.Models.MergePolicy>>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>>()))
            .ReturnsAsync(mockSubscription);

        var operation = new AddSubscriptionOperation(
            options,
            mockLogger.Object,
            mockBarClient.Object,
            mockRemoteFactory.Object,
            mockGitRepoFactory.Object);

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