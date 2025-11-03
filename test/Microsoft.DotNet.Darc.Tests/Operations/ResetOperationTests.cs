// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.Darc.Tests.Operations;

[TestFixture]
public class ResetOperationTests
{
    private Mock<IVmrInfo> _vmrInfoMock = null!;
    private Mock<IVmrUpdater> _vmrUpdaterMock = null!;
    private Mock<IVmrDependencyTracker> _dependencyTrackerMock = null!;
    private Mock<IProcessManager> _processManagerMock = null!;
    private Mock<IBarApiClient> _barClientMock = null!;
    private Mock<IRemote> _remoteMock = null!;
    private Mock<ILogger<ResetOperation>> _loggerMock = null!;

    [SetUp]
    public void Setup()
    {
        _vmrInfoMock = new Mock<IVmrInfo>();
        _vmrUpdaterMock = new Mock<IVmrUpdater>();
        _dependencyTrackerMock = new Mock<IVmrDependencyTracker>();
        _processManagerMock = new Mock<IProcessManager>();
        _barClientMock = new Mock<IBarApiClient>();
        _remoteMock = new Mock<IRemote>();
        _loggerMock = new Mock<ILogger<ResetOperation>>();

        // Setup default mock behaviors
        _vmrInfoMock.Setup(v => v.VmrPath).Returns(new NativePath("/test/vmr"));
    }

    [Test]
    public async Task ResetOperation_WithBuildAndChannel_ReturnsError()
    {
        var options = new ResetCommandLineOptions
        {
            Target = "runtime",
            Build = 12345,
            Channel = "test-channel",
            VmrPath = "/test/vmr"
        };

        var operation = new ResetOperation(
            options,
            _vmrInfoMock.Object,
            _vmrUpdaterMock.Object,
            _dependencyTrackerMock.Object,
            _processManagerMock.Object,
            _barClientMock.Object,
            _remoteMock.Object,
            _loggerMock.Object);

        int result = await operation.ExecuteAsync();

        result.Should().Be(Constants.ErrorCode);
    }

    [Test]
    public async Task ResetOperation_WithBuildAndColonInTarget_ReturnsError()
    {
        var options = new ResetCommandLineOptions
        {
            Target = "runtime:abc123",
            Build = 12345,
            VmrPath = "/test/vmr"
        };

        var operation = new ResetOperation(
            options,
            _vmrInfoMock.Object,
            _vmrUpdaterMock.Object,
            _dependencyTrackerMock.Object,
            _processManagerMock.Object,
            _barClientMock.Object,
            _remoteMock.Object,
            _loggerMock.Object);

        int result = await operation.ExecuteAsync();

        result.Should().Be(Constants.ErrorCode);
    }

    [Test]
    public async Task ResetOperation_WithChannelAndColonInTarget_ReturnsError()
    {
        var options = new ResetCommandLineOptions
        {
            Target = "runtime:abc123",
            Channel = "test-channel",
            VmrPath = "/test/vmr"
        };

        var operation = new ResetOperation(
            options,
            _vmrInfoMock.Object,
            _vmrUpdaterMock.Object,
            _dependencyTrackerMock.Object,
            _processManagerMock.Object,
            _barClientMock.Object,
            _remoteMock.Object,
            _loggerMock.Object);

        int result = await operation.ExecuteAsync();

        result.Should().Be(Constants.ErrorCode);
    }

    [Test]
    public async Task ResetOperation_WithInvalidFormatTarget_ReturnsError()
    {
        var options = new ResetCommandLineOptions
        {
            Target = "runtime",
            VmrPath = "/test/vmr"
        };

        var operation = new ResetOperation(
            options,
            _vmrInfoMock.Object,
            _vmrUpdaterMock.Object,
            _dependencyTrackerMock.Object,
            _processManagerMock.Object,
            _barClientMock.Object,
            _remoteMock.Object,
            _loggerMock.Object);

        int result = await operation.ExecuteAsync();

        result.Should().Be(Constants.ErrorCode);
    }

    [Test]
    public async Task ResetOperation_WithBuild_FetchesBuildFromBAR()
    {
        var options = new ResetCommandLineOptions
        {
            Target = "runtime",
            Build = 12345,
            VmrPath = "/test/vmr"
        };

        var mapping = new SourceMapping(
            "runtime",
            "https://github.com/dotnet/runtime",
            "main",
            [],
            [],
            false);
        var build = new Build(
            id: 12345,
            dateProduced: DateTimeOffset.UtcNow,
            staleness: 0,
            released: false,
            stable: false,
            commit: "abc123def456",
            channels: [],
            assets: [],
            dependencies: [],
            incoherencies: [])
        {
            GitHubRepository = "https://github.com/dotnet/runtime"
        };

        _dependencyTrackerMock.Setup(d => d.RefreshMetadataAsync(null)).Returns(Task.CompletedTask);
        _dependencyTrackerMock.Setup(d => d.GetMapping("runtime")).Returns(mapping);
        _dependencyTrackerMock.Setup(d => d.GetDependencyVersion(mapping))
            .Returns(new VmrDependencyVersion("oldsha"));
        _barClientMock.Setup(b => b.GetBuildAsync(12345)).ReturnsAsync(build);
        _remoteMock.Setup(r => r.GetSourceDependencyAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new Exception("Version.Details.xml not found"));

        var operation = new ResetOperation(
            options,
            _vmrInfoMock.Object,
            _vmrUpdaterMock.Object,
            _dependencyTrackerMock.Object,
            _processManagerMock.Object,
            _barClientMock.Object,
            _remoteMock.Object,
            _loggerMock.Object);

        // We expect this to fail because we haven't mocked all the necessary operations
        // but we can verify that GetBuildAsync was called
        int result = await operation.ExecuteAsync();

        _barClientMock.Verify(b => b.GetBuildAsync(12345), Times.Once);
    }

    [Test]
    public async Task ResetOperation_WithChannel_FetchesLatestBuildFromChannel()
    {
        var options = new ResetCommandLineOptions
        {
            Target = "runtime",
            Channel = ".NET 9",
            VmrPath = "/test/vmr"
        };

        var mapping = new SourceMapping(
            "runtime",
            "https://github.com/dotnet/runtime",
            "main",
            [],
            [],
            false);
        var channel = new Channel(id: 1, name: ".NET 9", classification: "product");
        var build = new Build(
            id: 12345,
            dateProduced: DateTimeOffset.UtcNow,
            staleness: 0,
            released: false,
            stable: false,
            commit: "abc123def456",
            channels: [],
            assets: [],
            dependencies: [],
            incoherencies: [])
        {
            GitHubRepository = "https://github.com/dotnet/runtime"
        };

        _dependencyTrackerMock.Setup(d => d.RefreshMetadataAsync(null)).Returns(Task.CompletedTask);
        _dependencyTrackerMock.Setup(d => d.GetMapping("runtime")).Returns(mapping);
        _dependencyTrackerMock.Setup(d => d.GetDependencyVersion(mapping))
            .Returns(new VmrDependencyVersion("oldsha"));
        _barClientMock.Setup(b => b.GetChannelsAsync(null))
            .ReturnsAsync([channel]);
        _barClientMock.Setup(b => b.GetLatestBuildAsync(mapping.DefaultRemote, channel.Id))
            .ReturnsAsync(build);

        var operation = new ResetOperation(
            options,
            _vmrInfoMock.Object,
            _vmrUpdaterMock.Object,
            _dependencyTrackerMock.Object,
            _processManagerMock.Object,
            _barClientMock.Object,
            _remoteMock.Object,
            _loggerMock.Object);

        // We expect this to fail because we haven't mocked all the necessary operations
        // but we can verify that GetChannelsAsync and GetLatestBuildAsync were called
        int result = await operation.ExecuteAsync();

        _barClientMock.Verify(b => b.GetChannelsAsync(null), Times.Once);
        _barClientMock.Verify(b => b.GetLatestBuildAsync(mapping.DefaultRemote, channel.Id), Times.Once);
    }
}
