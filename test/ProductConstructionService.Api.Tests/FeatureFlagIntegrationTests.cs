// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using ProductConstructionService.Common;

namespace ProductConstructionService.Api.Tests;

[TestFixture]
public class FeatureFlagIntegrationTests
{
    private Mock<IFeatureFlagClient> _mockFeatureFlagClient = null!;
    private Mock<ILogger<FeatureFlagIntegrationExample>> _mockLogger = null!;
    private FeatureFlagIntegrationExample _integrationExample = null!;
    private Guid _testSubscriptionId;

    [SetUp]
    public void Setup()
    {
        _mockFeatureFlagClient = new Mock<IFeatureFlagClient>();
        _mockLogger = new Mock<ILogger<FeatureFlagIntegrationExample>>();
        _integrationExample = new FeatureFlagIntegrationExample(_mockFeatureFlagClient.Object, _mockLogger.Object);
        _testSubscriptionId = Guid.NewGuid();
    }

    [Test]
    public async Task ProcessPullRequestUpdateAsync_RebaseEnabled_ReturnsExpectedResult()
    {
        // Arrange
        _mockFeatureFlagClient
            .Setup(c => c.GetBooleanFlagAsync(FeatureFlags.EnableRebaseStrategy, false))
            .ReturnsAsync(true);

        // Act
        var result = await _integrationExample.ProcessPullRequestUpdateAsync(_testSubscriptionId);

        // Assert
        result.Should().Contain("Rebase strategy enabled");

        // Verify that the feature flag client was initialized
        _mockFeatureFlagClient.Verify(c => c.InitializeAsync(_testSubscriptionId, default), Times.Once);
    }

    [Test]
    public async Task ProcessPullRequestUpdateAsync_RebaseDisabled_ReturnsStandardResult()
    {
        // Arrange
        _mockFeatureFlagClient
            .Setup(c => c.GetBooleanFlagAsync(FeatureFlags.EnableRebaseStrategy, false))
            .ReturnsAsync(false);

        // Act
        var result = await _integrationExample.ProcessPullRequestUpdateAsync(_testSubscriptionId);

        // Assert
        result.Should().Be("Standard merge processing.");

        // Verify that the feature flag client was initialized
        _mockFeatureFlagClient.Verify(c => c.InitializeAsync(_testSubscriptionId, default), Times.Once);
    }

    [Test]
    public async Task GetConflictResolutionStrategyAsync_RebaseEnabled_ReturnsRebaseStrategy()
    {
        // Arrange
        _mockFeatureFlagClient
            .Setup(c => c.GetBooleanFlagAsync(FeatureFlags.EnableRebaseStrategy, false))
            .ReturnsAsync(true);

        // Act
        var useRebaseStrategy = await _integrationExample.GetConflictResolutionStrategyAsync(_testSubscriptionId);

        // Assert
        useRebaseStrategy.Should().BeTrue();

        // Verify that the feature flag client was initialized
        _mockFeatureFlagClient.Verify(c => c.InitializeAsync(_testSubscriptionId, default), Times.Once);
    }

    [Test]
    public async Task GetConflictResolutionStrategyAsync_RebaseDisabled_ReturnsMergeStrategy()
    {
        // Arrange
        _mockFeatureFlagClient
            .Setup(c => c.GetBooleanFlagAsync(FeatureFlags.EnableRebaseStrategy, false))
            .ReturnsAsync(false);

        // Act
        var useRebaseStrategy = await _integrationExample.GetConflictResolutionStrategyAsync(_testSubscriptionId);

        // Assert
        useRebaseStrategy.Should().BeFalse();

        // Verify that all expected flags were queried
        _mockFeatureFlagClient.Verify(
            c => c.GetBooleanFlagAsync(FeatureFlags.EnableRebaseStrategy, false),
            Times.Once);
    }
}