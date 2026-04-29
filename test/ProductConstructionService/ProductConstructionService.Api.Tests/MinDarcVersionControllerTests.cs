// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using Maestro.Common;
using Maestro.Services.Common.Cache;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ProductConstructionService.Api.Api;
using ProductConstructionService.Api.Controllers;

namespace ProductConstructionService.Api.Tests;

[TestFixture]
public class MinDarcVersionControllerTests
{
    private Mock<IRedisCacheFactory> _mockFactory = null!;
    private Mock<IRedisCache> _mockCache = null!;
    private MinDarcVersionController _controller = null!;

    [SetUp]
    public void Setup()
    {
        _mockFactory = new Mock<IRedisCacheFactory>();
        _mockCache = new Mock<IRedisCache>();
        _mockFactory
            .Setup(f => f.Create(MinClientVersionConstants.DarcMinVersionRedisKey))
            .Returns(_mockCache.Object);
        _controller = new MinDarcVersionController(_mockFactory.Object);
    }

    [Test]
    public async Task GetMinDarcVersion_WhenSet_ReturnsOkWithVersion()
    {
        // Arrange
        _mockCache.Setup(c => c.TryGetAsync()).ReturnsAsync("1.2.3");

        // Act
        var result = await _controller.GetMinDarcVersionAsync();

        // Assert
        result.Should().BeAssignableTo<OkObjectResult>();
        ((OkObjectResult)result).Value.Should().Be("1.2.3");
    }

    [Test]
    public async Task GetMinDarcVersion_WhenUnset_ReturnsNoContent()
    {
        // Arrange
        _mockCache.Setup(c => c.TryGetAsync()).ReturnsAsync((string?)null);

        // Act
        var result = await _controller.GetMinDarcVersionAsync();

        // Assert
        result.Should().BeAssignableTo<NoContentResult>();
    }

    [Test]
    public async Task SetMinDarcVersion_ValidVersion_StoresAndReturnsOk()
    {
        // Act
        var result = await _controller.SetMinDarcVersionAsync("1.2.3");

        // Assert
        result.Should().BeAssignableTo<OkResult>();
        _mockCache.Verify(c => c.SetAsync("1.2.3", null), Times.Once);
    }

    [Test]
    public async Task SetMinDarcVersion_InvalidVersion_ReturnsBadRequestAndDoesNotStore()
    {
        // Act
        var result = await _controller.SetMinDarcVersionAsync("not-a-version!!");

        // Assert
        result.Should().BeAssignableTo<BadRequestObjectResult>();
        ((BadRequestObjectResult)result).Value.Should().BeOfType<ApiError>();
        _mockCache.Verify(c => c.SetAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>()), Times.Never);
    }

    [Test]
    public async Task SetMinDarcVersion_EmptyVersion_ReturnsBadRequestAndDoesNotStore()
    {
        // Act
        var result = await _controller.SetMinDarcVersionAsync("");

        // Assert
        result.Should().BeAssignableTo<BadRequestObjectResult>();
        _mockCache.Verify(c => c.SetAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>()), Times.Never);
    }

    [Test]
    public async Task ClearMinDarcVersion_CallsTryDeleteAndReturnsNoContent()
    {
        // Arrange
        _mockCache.Setup(c => c.TryDeleteAsync()).ReturnsAsync(true);

        // Act
        var result = await _controller.ClearMinDarcVersionAsync();

        // Assert
        result.Should().BeAssignableTo<NoContentResult>();
        _mockCache.Verify(c => c.TryDeleteAsync(), Times.Once);
    }

    [Test]
    public async Task ClearMinDarcVersion_KeyMissing_StillReturnsNoContent()
    {
        // Arrange
        _mockCache.Setup(c => c.TryDeleteAsync()).ReturnsAsync(false);

        // Act
        var result = await _controller.ClearMinDarcVersionAsync();

        // Assert
        result.Should().BeAssignableTo<NoContentResult>();
        _mockCache.Verify(c => c.TryDeleteAsync(), Times.Once);
    }
}
