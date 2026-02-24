// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using AwesomeAssertions;
using Microsoft.DotNet.Darc.Helpers;
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
using System.Threading.Tasks;

namespace Microsoft.DotNet.Darc.Tests.Operations;

[TestFixture]
public class GetBuildOperationTests
{
    private ConsoleOutputIntercepter _consoleOutput = null!;
    private Mock<IBarApiClient> _barMock = null!;
    private Mock<ILogger<GetBuildOperation>> _loggerMock = null!;

    [SetUp]
    public void Setup()
    {
        _consoleOutput = new();
        _barMock = new Mock<IBarApiClient>();
        _loggerMock = new();
    }

    [TearDown]
    public void Teardown()
    {
        _consoleOutput.Dispose();
    }

    [Test]
    public async Task GetBuildOperationShouldHandleDuplicateBuilds()
    {
        string repo = "repo";
        string sha = "50c88957fb93ccaa0040b5b28ff459a29ecf88c6";
        string internalRepo = $"internal-{repo}";
        string githubRepo = $"Github-{repo}";
        Subscription subscription1 = new(Guid.Empty, true, false, internalRepo, "target", "test", string.Empty, null, null, []);
        Subscription subscription2 = new(Guid.Empty, true, false, githubRepo, "target", "test", string.Empty, null, null, []);

        List<Subscription> subscriptions =
        [
            subscription1,
            subscription2
        ];

        ProductConstructionService.Client.Models.Build build = new(
            id: 0,
            dateProduced: DateTimeOffset.Now,
            staleness: 0,
            released: false,
            stable: false,
            commit: sha,
            channels: [],
            assets: [],
            dependencies: [],
            incoherencies: [])
        {
            AzureDevOpsRepository = internalRepo,
            GitHubRepository = githubRepo,
        };

        List<ProductConstructionService.Client.Models.Build> builds =
        [
            build
        ];

        _barMock.Setup(t => t.GetSubscriptionsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool?>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(subscriptions.AsEnumerable());
        _barMock.Setup(t => t.GetBuildsAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(builds.AsEnumerable());

        GetBuildCommandLineOptions options = new()
        {
            Repo = repo,
            Commit = sha
        };

        GetBuildOperation getBuildOperation = new(options, _barMock.Object, _loggerMock.Object);

        int result = await getBuildOperation.ExecuteAsync();

        result.Should().Be(Constants.SuccessCode);

        var output = _consoleOutput.GetOutput();
        output.Should().Be(UxHelpers.GetTextBuildDescription(build));
    }

    [Test]
    public async Task GetBuildOperationShouldFetchById()
    {
        string repo = "repo";
        int buildId = 10001;
        string sha = "50c88957fb93ccaa0040b5b28ff459a29ecf88c6";
        string internalRepo = $"internal-{repo}";
        string githubRepo = $"Github-{repo}";

        ProductConstructionService.Client.Models.Build build = new(
            id: buildId,
            dateProduced: DateTimeOffset.Now,
            staleness: 0,
            released: false,
            stable: false,
            commit: sha,
            channels: [],
            assets: [],
            dependencies: [],
            incoherencies: [])
        {
            AzureDevOpsRepository = internalRepo,
            GitHubRepository = githubRepo,
        };

        _barMock.Setup(t => t.GetBuildAsync(It.IsAny<int>()))
            .ReturnsAsync(build);

        GetBuildCommandLineOptions options = new()
        {
            Id = buildId
        };

        GetBuildOperation getBuildOperation = new(options, _barMock.Object, _loggerMock.Object);

        int result = await getBuildOperation.ExecuteAsync();

        result.Should().Be(Constants.SuccessCode);

        var output = _consoleOutput.GetOutput();
        output.Should().Be(UxHelpers.GetTextBuildDescription(build));
    }


    [Test]
    public async Task GetBuildOperationShouldWorkWhenDoseNotFindId()
    {
        int buildId = 10001;

        _barMock.Setup(t => t.GetBuildAsync(It.IsAny<int>()))
            .Throws(new Exception());

        GetBuildCommandLineOptions options = new()
        {
            Id = buildId
        };

        GetBuildOperation getBuildOperation = new(options, _barMock.Object, _loggerMock.Object);

        int result = await getBuildOperation.ExecuteAsync();

        result.Should().Be(Constants.ErrorCode);
    }
}
