// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using ProductConstructionService.Api.Api.v2020_02_20.Controllers;
using ProductConstructionService.Common;

namespace ProductConstructionService.Api.Tests;

[TestFixture]
public class FeatureFlagsController20200220Tests
{
    private Mock<IFeatureFlagService> _mockFeatureFlagService = null!;
    private Mock<ILogger<FeatureFlagsController>> _mockLogger = null!;
    private FeatureFlagsController _controller = null!;
    private Guid _testSubscriptionId;

    [SetUp]
    public void Setup()
    {
        _mockFeatureFlagService = new Mock<IFeatureFlagService>();
        _mockLogger = new Mock<ILogger<FeatureFlagsController>>();
        _controller = new FeatureFlagsController(_mockFeatureFlagService.Object, _mockLogger.Object);
        _testSubscriptionId = Guid.NewGuid();
    }

    [Test]
    public async Task SetFeatureFlag_InvalidFlag_ReturnsBadRequest()
    {
        // Arrange
        var request = new SetFeatureFlagRequest(_testSubscriptionId, "invalid-flag", "true");
        var expectedResponse = new FeatureFlagResponse(false, "Unknown feature flag: invalid-flag");

        // Act
        var result = await _controller.SetFeatureFlag(request);

        // Assert
        result.Should().BeAssignableTo<BadRequestObjectResult>();
    }

    [Test]
    public async Task SetFeatureFlag_ValidRequest_ReturnsSuccess()
    {
        // Arrange
        var request = new SetFeatureFlagRequest(
            _testSubscriptionId,
            FeatureFlag.EnableRebaseStrategy.Name,
            "true");

        var expectedResponse = new FeatureFlagResponse(
            true,
            "Feature flag set successfully",
            new FeatureFlagValue(_testSubscriptionId, FeatureFlag.EnableRebaseStrategy.Name, "true"));

        _mockFeatureFlagService
            .Setup(s => s.SetFlagAsync(_testSubscriptionId, FeatureFlag.EnableRebaseStrategy, "true", null, default))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.SetFeatureFlag(request);

        // Assert
        result.Should().BeAssignableTo<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        okResult.Value.Should().BeEquivalentTo(expectedResponse);
        
        _mockFeatureFlagService.Verify(
            s => s.SetFlagAsync(_testSubscriptionId, FeatureFlag.EnableRebaseStrategy, "true", null, default),
            Times.Once);
    }

    [Test]
    public async Task SetFeatureFlag_NullRequest_ReturnsBadRequest()
    {
        // Act
        var result = await _controller.SetFeatureFlag(null!);

        // Assert
        result.Should().BeAssignableTo<BadRequestObjectResult>();
        var badRequestResult = (BadRequestObjectResult)result;
        var response = badRequestResult.Value as FeatureFlagResponse;
        response!.Success.Should().BeFalse();
        response.Message.Should().Be("Request cannot be null");
    }

    [Test]
    public async Task GetFeatureFlag_ExistingFlag_ReturnsFlag()
    {
        // Arrange
        var expectedFlag = new FeatureFlagValue(_testSubscriptionId, FeatureFlag.EnableRebaseStrategy.Name, "true");

        _mockFeatureFlagService
            .Setup(s => s.GetFlagAsync(_testSubscriptionId, FeatureFlag.EnableRebaseStrategy, default))
            .ReturnsAsync(expectedFlag);

        // Act
        var result = await _controller.GetFeatureFlag(_testSubscriptionId, FeatureFlag.EnableRebaseStrategy.Name);

        // Assert
        result.Should().BeAssignableTo<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        okResult.Value.Should().BeEquivalentTo(expectedFlag);
    }

    [Test]
    public async Task GetFeatureFlag_NonExistentFlag_ReturnsNotFound()
    {
        // Arrange
        _mockFeatureFlagService
            .Setup(s => s.GetFlagAsync(_testSubscriptionId, FeatureFlag.EnableRebaseStrategy, default))
            .ReturnsAsync((FeatureFlagValue?)null);

        // Act
        var result = await _controller.GetFeatureFlag(_testSubscriptionId, FeatureFlag.EnableRebaseStrategy.Name);

        // Assert
        result.Should().BeAssignableTo<NotFoundResult>();
    }

    [Test]
    public async Task GetFeatureFlags_ExistingSubscription_ReturnsFlags()
    {
        // Arrange
        var expectedFlags = new List<FeatureFlagValue>
        {
            new(_testSubscriptionId, FeatureFlag.EnableRebaseStrategy.Name, "true")
        };

        _mockFeatureFlagService
            .Setup(s => s.GetFlagsForSubscriptionAsync(_testSubscriptionId, default))
            .ReturnsAsync(expectedFlags);

        // Act
        var result = await _controller.GetFeatureFlags(_testSubscriptionId);

        // Assert
        result.Should().BeAssignableTo<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var response = okResult.Value as FeatureFlagListResponse;
        response!.Flags.Should().BeEquivalentTo(expectedFlags);
        response.Total.Should().Be(1);
    }

    [Test]
    public async Task RemoveFeatureFlag_ExistingFlag_ReturnsSuccess()
    {
        // Arrange
        _mockFeatureFlagService
            .Setup(s => s.RemoveFlagAsync(_testSubscriptionId, FeatureFlag.EnableRebaseStrategy, default))
            .ReturnsAsync(true);

        // Act
        var result = await _controller.RemoveFeatureFlag(_testSubscriptionId, FeatureFlag.EnableRebaseStrategy.Name);

        // Assert
        result.Should().BeAssignableTo<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        okResult.Value.Should().Be(true);
    }

    [Test]
    public async Task RemoveFeatureFlag_NonExistentFlag_ReturnsNotFound()
    {
        // Arrange
        _mockFeatureFlagService
            .Setup(s => s.RemoveFlagAsync(_testSubscriptionId, FeatureFlag.EnableRebaseStrategy, default))
            .ReturnsAsync(false);

        // Act
        var result = await _controller.RemoveFeatureFlag(_testSubscriptionId, FeatureFlag.EnableRebaseStrategy.Name);

        // Assert
        result.Should().BeAssignableTo<NotFoundResult>();
    }

    [Test]
    public void GetAvailableFeatureFlags_ReturnsAllFlags()
    {
        // Act
        var result = _controller.GetAvailableFeatureFlags();

        // Assert
        result.Should().BeAssignableTo<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var response = okResult.Value as AvailableFeatureFlagsResponse;
        response!.Flags.Should().HaveCount(FeatureFlags.AllFlags.Count);
        response.Flags.Should().Contain(FeatureFlag.EnableRebaseStrategy.Name);
    }
}
