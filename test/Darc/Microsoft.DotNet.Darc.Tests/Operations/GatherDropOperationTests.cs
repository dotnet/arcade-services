// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.Darc.Tests.Helpers;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Darc.Tests.Operations;

[TestFixture]
public class GatherDropOperationTests
{
    private const string RepoUri = "https://github.com/dotnet/maui";
    private const string Commit = "50c88957fb93ccaa0040b5b28ff459a29ecf88c6";
    private const string WorkloadChannelName = ".NET 10 Workload Release";

    private static readonly Channel WorkloadChannel = new(1, WorkloadChannelName, "release");
    private static readonly Channel InternalChannel = new(2, ".NET 10 Internal", "internal");
    private static readonly Channel PublicChannel = new(3, ".NET 10", "product");

    private ConsoleOutputIntercepter _consoleOutput;
    private Mock<IBarApiClient> _barMock;

    [SetUp]
    public void Setup()
    {
        _consoleOutput = new();
        _barMock = new Mock<IBarApiClient>();
    }

    [TearDown]
    public void Teardown()
    {
        _consoleOutput.Dispose();
    }

    [Test]
    public async Task GetRootBuildsAsync_CommitAndChannel_ReturnsSingleBuildOnMatchingChannel()
    {
        // Arrange
        Build workloadBuild = CreateBuild(101, WorkloadChannel, PublicChannel);
        SetupBuildsOfCommit(CreateBuild(100, InternalChannel), workloadBuild, CreateBuild(102, PublicChannel));
        GatherDropOperation operation = CreateOperation(commit: Commit, channel: "workload release");

        // Act
        IEnumerable<Build> rootBuilds = await operation.GetRootBuildsAsync();

        // Assert
        rootBuilds.Should().ContainSingle().Which.Should().BeSameAs(workloadBuild);
        _barMock.Verify(b => b.GetChannelsAsync(It.IsAny<string>()), Times.Never);
        _barMock.Verify(b => b.GetLatestBuildAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task GetRootBuildsAsync_CommitAndChannel_NoBuildOnMatchingChannel_ReturnsNullAndListsAllCandidates()
    {
        // Arrange
        SetupBuildsOfCommit(CreateBuild(100, InternalChannel), CreateBuild(102, PublicChannel));
        GatherDropOperation operation = CreateOperation(commit: Commit, channel: "Workload");

        // Act
        IEnumerable<Build> rootBuilds = await operation.GetRootBuildsAsync();

        // Assert
        rootBuilds.Should().BeNull();
        string output = _consoleOutput.GetOutput();
        output.Should().Contain("assigned to a channel matching 'Workload'");
        output.Should().Contain("100: 20261124.100").And.Contain(InternalChannel.Name);
        output.Should().Contain("102: 20261124.102").And.Contain(PublicChannel.Name);
    }

    [Test]
    public async Task GetRootBuildsAsync_CommitAndChannel_MultipleBuildsOnMatchingChannel_ReturnsNullAndSuggestsId()
    {
        // Arrange
        SetupBuildsOfCommit(CreateBuild(100, InternalChannel), CreateBuild(101, WorkloadChannel), CreateBuild(102, WorkloadChannel));
        GatherDropOperation operation = CreateOperation(commit: Commit, channel: "Workload");

        // Act
        IEnumerable<Build> rootBuilds = await operation.GetRootBuildsAsync();

        // Assert
        rootBuilds.Should().BeNull();
        string output = _consoleOutput.GetOutput();
        output.Should().Contain("There were 2 potential root builds on a channel matching 'Workload'").And.Contain("--id");
        output.Should().Contain("101: 20261124.101").And.Contain("102: 20261124.102");
        output.Should().NotContain("100: 20261124.100");
    }

    [Test]
    public async Task GetRootBuildsAsync_CommitOnly_MultipleBuilds_ListsChannelsAndSuggestsChannelOption()
    {
        // Arrange
        SetupBuildsOfCommit(CreateBuild(100, InternalChannel), CreateBuild(101, WorkloadChannel));
        GatherDropOperation operation = CreateOperation(commit: Commit, channel: null);

        // Act
        IEnumerable<Build> rootBuilds = await operation.GetRootBuildsAsync();

        // Assert
        rootBuilds.Should().BeNull();
        string output = _consoleOutput.GetOutput();
        output.Should().Contain("There were 2 potential root builds.").And.Contain("--channel");
        output.Should().Contain("100: 20261124.100 @").And.Contain(InternalChannel.Name);
        output.Should().Contain("101: 20261124.101 @").And.Contain(WorkloadChannelName);
    }

    [Test]
    public async Task GetRootBuildsAsync_CommitAndChannelWithoutRepo_FailsValidation()
    {
        // Arrange
        GatherDropOperation operation = CreateOperation(commit: Commit, channel: "Workload", repoUri: null);

        // Act
        IEnumerable<Build> rootBuilds = await operation.GetRootBuildsAsync();

        // Assert
        rootBuilds.Should().BeNull();
        _consoleOutput.GetOutput().Should().Contain("--repo is required");
        _barMock.Verify(b => b.GetBuildsAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    private GatherDropOperation CreateOperation(string commit, string channel, string repoUri = RepoUri)
    {
        GatherDropCommandLineOptions options = new()
        {
            RootBuildIds = [],
            RepoUri = repoUri,
            Commit = commit,
            Channel = channel,
        };

        return new GatherDropOperation(
            options,
            Mock.Of<ILogger<GatherDropOperation>>(),
            _barMock.Object,
            Mock.Of<IRemoteFactory>(),
            Mock.Of<ILocalFactory>());
    }

    private void SetupBuildsOfCommit(params Build[] builds)
    {
        _barMock.Setup(b => b.GetBuildsAsync(RepoUri, Commit))
            .ReturnsAsync(builds.AsEnumerable());
    }

    private static Build CreateBuild(int id, params Channel[] channels)
    {
        return new Build(
            id: id,
            dateProduced: DateTimeOffset.Now,
            staleness: 0,
            released: false,
            stable: false,
            commit: Commit,
            channels: [.. channels],
            assets: [],
            dependencies: [],
            incoherencies: [])
        {
            GitHubRepository = RepoUri,
            AzureDevOpsBuildNumber = $"20261124.{id}",
        };
    }

    // IsBlobFeedUrl is a private static method, so we need to use reflection to test it
    private static bool InvokeIsBlobFeedUrl(string location)
    {
        var type = typeof(GatherDropOperation);
        var method = type.GetMethod("IsBlobFeedUrl", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("IsBlobFeedUrl method should exist");
        return (bool)method.Invoke(null, [location]);
    }

    [TestCase("https://dotnetfeed.blob.core.windows.net/dotnet-core/index.json", ExpectedResult = true)]
    [TestCase("https://dotnetcli.blob.core.windows.net/dotnet/index.json", ExpectedResult = true)]
    [TestCase("https://dotnetclichecksums.blob.core.windows.net/dotnet/index.json", ExpectedResult = true)]
    public bool IsBlobFeedUrl_RecognizesBlobCoreWindowsNet(string location)
    {
        return InvokeIsBlobFeedUrl(location);
    }

    [TestCase("https://ci.dot.net/public", ExpectedResult = true)]
    [TestCase("https://ci.dot.net/internal", ExpectedResult = true)]
    [TestCase("https://ci.dot.net/public/", ExpectedResult = true)]
    [TestCase("https://ci.dot.net/internal/", ExpectedResult = true)]
    [TestCase("https://CI.DOT.NET/public", ExpectedResult = true)]
    [TestCase("https://Ci.Dot.Net/internal", ExpectedResult = true)]
    public bool IsBlobFeedUrl_RecognizesCiDotNet(string location)
    {
        return InvokeIsBlobFeedUrl(location);
    }

    [TestCase("https://pkgs.dev.azure.com/dnceng/public/_packaging/public-feed-name/nuget/v3/index.json", ExpectedResult = false)]
    [TestCase("https://pkgs.dev.azure.com/dnceng/_packaging/internal-feed-name/nuget/v3/index.json", ExpectedResult = false)]
    [TestCase("https://www.nuget.org/api/v2", ExpectedResult = false)]
    [TestCase("https://example.com/some-feed", ExpectedResult = false)]
    [TestCase("not-a-valid-uri", ExpectedResult = false)]
    [TestCase("", ExpectedResult = false)]
    public bool IsBlobFeedUrl_DoesNotRecognizeNonBlobUrls(string location)
    {
        return InvokeIsBlobFeedUrl(location);
    }
}
