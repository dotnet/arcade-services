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
    public async Task ProcessPullRequestUpdateAsync_AllFlagsEnabled_ReturnsExpectedResult()
    {
        // Arrange
        _mockFeatureFlagClient
            .Setup(c => c.GetBooleanFlagAsync(FeatureFlags.EnableEnhancedPrUpdates, false))
            .ReturnsAsync(true);
            
        _mockFeatureFlagClient
            .Setup(c => c.GetBooleanFlagAsync(FeatureFlags.EnableBatchDependencyUpdates, false))
            .ReturnsAsync(true);
            
        _mockFeatureFlagClient
            .Setup(c => c.GetBooleanFlagAsync(FeatureFlags.EnableDetailedTelemetry, false))
            .ReturnsAsync(true);

        // Act
        var result = await _integrationExample.ProcessPullRequestUpdateAsync(_testSubscriptionId);

        // Assert
        result.Should().Contain("Enhanced PR processing enabled");
        result.Should().Contain("Batch processing enabled");
        result.Should().Contain("Detailed telemetry enabled");

        // Verify that the feature flag client was initialized
        _mockFeatureFlagClient.Verify(c => c.InitializeAsync(_testSubscriptionId, default), Times.Once);
    }

    [Test]
    public async Task ProcessPullRequestUpdateAsync_AllFlagsDisabled_ReturnsStandardResult()
    {
        // Arrange
        _mockFeatureFlagClient
            .Setup(c => c.GetBooleanFlagAsync(It.IsAny<string>(), false))
            .ReturnsAsync(false);

        // Act
        var result = await _integrationExample.ProcessPullRequestUpdateAsync(_testSubscriptionId);

        // Assert
        result.Should().Be("Standard PR processing.");

        // Verify that the feature flag client was initialized
        _mockFeatureFlagClient.Verify(c => c.InitializeAsync(_testSubscriptionId, default), Times.Once);
    }

    [Test]
    public async Task GetSubscriptionConfigAsync_MixedFlags_ReturnsCorrectConfiguration()
    {
        // Arrange
        _mockFeatureFlagClient
            .Setup(c => c.GetBooleanFlagAsync(FeatureFlags.EnableAdvancedMergeConflictResolution, false))
            .ReturnsAsync(true);
            
        _mockFeatureFlagClient
            .Setup(c => c.GetBooleanFlagAsync(FeatureFlags.EnableExperimentalDependencyFlow, false))
            .ReturnsAsync(false);

        // Act
        var (useAdvancedMerge, useExperimentalFlow) = await _integrationExample.GetSubscriptionConfigAsync(_testSubscriptionId);

        // Assert
        useAdvancedMerge.Should().BeTrue();
        useExperimentalFlow.Should().BeFalse();

        // Verify that the feature flag client was initialized
        _mockFeatureFlagClient.Verify(c => c.InitializeAsync(_testSubscriptionId, default), Times.Once);
    }

    [Test]
    public async Task GetSubscriptionConfigAsync_BothFlagsEnabled_ReturnsBothTrue()
    {
        // Arrange
        _mockFeatureFlagClient
            .Setup(c => c.GetBooleanFlagAsync(It.IsAny<string>(), false))
            .ReturnsAsync(true);

        // Act
        var (useAdvancedMerge, useExperimentalFlow) = await _integrationExample.GetSubscriptionConfigAsync(_testSubscriptionId);

        // Assert
        useAdvancedMerge.Should().BeTrue();
        useExperimentalFlow.Should().BeTrue();

        // Verify that all expected flags were queried
        _mockFeatureFlagClient.Verify(
            c => c.GetBooleanFlagAsync(FeatureFlags.EnableAdvancedMergeConflictResolution, false),
            Times.Once);
        _mockFeatureFlagClient.Verify(
            c => c.GetBooleanFlagAsync(FeatureFlags.EnableExperimentalDependencyFlow, false),
            Times.Once);
    }
}