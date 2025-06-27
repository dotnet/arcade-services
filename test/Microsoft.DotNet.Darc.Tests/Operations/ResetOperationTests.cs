// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Darc.Tests.Operations;

[TestFixture]
public class ResetOperationTests
{
    private Mock<IVmrUpdater> _vmrUpdaterMock = null!;
    private Mock<IVmrDependencyTracker> _dependencyTrackerMock = null!;
    private Mock<ILogger<ResetOperation>> _loggerMock = null!;

    [SetUp]
    public void Setup()
    {
        _vmrUpdaterMock = new Mock<IVmrUpdater>();
        _dependencyTrackerMock = new Mock<IVmrDependencyTracker>();
        _loggerMock = new Mock<ILogger<ResetOperation>>();
    }

    [Test]
    public async Task ResetOperation_ExecuteAsync_ReturnsErrorCodeForInvalidFormat()
    {
        // Arrange
        var options = new ResetCommandLineOptions
        {
            MappingAndSha = "invalid-format-without-colon"
        };

        var operation = new ResetOperation(options, _vmrUpdaterMock.Object, _dependencyTrackerMock.Object, _loggerMock.Object);

        // Act
        var result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.ErrorCode);
    }

    [Test]
    public async Task ResetOperation_ExecuteAsync_ReturnsErrorCodeForEmptyMapping()
    {
        // Arrange  
        var options = new ResetCommandLineOptions
        {
            MappingAndSha = ":abc123"
        };

        var operation = new ResetOperation(options, _vmrUpdaterMock.Object, _dependencyTrackerMock.Object, _loggerMock.Object);

        // Act
        var result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.ErrorCode);
    }

    [Test]
    public async Task ResetOperation_ExecuteAsync_ReturnsErrorCodeForEmptySha()
    {
        // Arrange
        var options = new ResetCommandLineOptions
        {
            MappingAndSha = "mapping:"
        };

        var operation = new ResetOperation(options, _vmrUpdaterMock.Object, _dependencyTrackerMock.Object, _loggerMock.Object);

        // Act
        var result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.ErrorCode);
    }
}