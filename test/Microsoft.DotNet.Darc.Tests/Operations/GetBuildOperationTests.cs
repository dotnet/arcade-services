// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using FluentAssertions;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.Darc.Tests.Helpers;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Darc.Tests.Operations;

[TestFixture]
public class GetBuildOperationTests
{
    private ConsoleOutputIntercepter _consoleOutput = null!;
    private ServiceCollection _services = null!;
    private Mock<IRemote> _remoteMock = null!;

    [SetUp]
    public void Setup()
    {
        _consoleOutput = new();
        _remoteMock = new Mock<IRemote>();
        _services = new ServiceCollection();
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
        Subscription subscription1 = new(Guid.Empty, true, internalRepo, "target", "test", string.Empty);
        Subscription subscription2 = new(Guid.Empty, true, githubRepo, "target", "test", string.Empty);

        List<Subscription> subscriptions = new()
        {
            subscription1,
            subscription2
        };

        Build build = new(
            id: 0,
            dateProduced: DateTimeOffset.Now,
            staleness: 0,
            released: false,
            stable: false,
            commit: sha,
            channels: ImmutableList.Create<Channel>(),
            assets: ImmutableList.Create<Asset>(),
            dependencies: ImmutableList.Create<BuildRef>(),
            incoherencies: ImmutableList.Create<BuildIncoherence>()
            )
        {
            AzureDevOpsRepository = internalRepo,
            GitHubRepository = githubRepo,
        };

        List<Build> builds = new()
        {
            build
        };

        _remoteMock.Setup(t => t.GetSubscriptionsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()))
            .Returns(Task.FromResult(subscriptions.AsEnumerable()));
        _remoteMock.Setup(t => t.GetBuildsAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.FromResult(builds.AsEnumerable()));
        _services.AddSingleton(_remoteMock.Object);

        GetBuildCommandLineOptions options = new()
        {
            Repo = repo,
            Commit = sha
        };

        GetBuildOperation getBuildOperation = new(options, _services);

        int result = await getBuildOperation.ExecuteAsync();

        result.Should().Be(Constants.SuccessCode);

        var output = _consoleOutput.GetOuput();
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
        
        Build build = new(
            id: buildId,
            dateProduced: DateTimeOffset.Now,
            staleness: 0,
            released: false,
            stable: false,
            commit: sha,
            channels: ImmutableList.Create<Channel>(),
            assets: ImmutableList.Create<Asset>(),
            dependencies: ImmutableList.Create<BuildRef>(),
            incoherencies: ImmutableList.Create<BuildIncoherence>()
            )
        {
            AzureDevOpsRepository = internalRepo,
            GitHubRepository = githubRepo,
        };

        _remoteMock.Setup(t => t.GetBuildAsync(It.IsAny<int>()))
            .Returns(Task.FromResult(build));

        _services.AddSingleton(_remoteMock.Object);

        GetBuildCommandLineOptions options = new()
        {
            Id = buildId
        };

        GetBuildOperation getBuildOperation = new(options, _services);

        int result = await getBuildOperation.ExecuteAsync();

        result.Should().Be(Constants.SuccessCode);

        var output = _consoleOutput.GetOuput();
        output.Should().Be(UxHelpers.GetTextBuildDescription(build));
    }


    [Test]
    public async Task GetBuildOperationShouldWorkWhenDoseNotFindId()
    {
        int buildId = 10001;

        _remoteMock.Setup(t => t.GetBuildAsync(It.IsAny<int>()))
            .Throws(new Exception());

        _services.AddSingleton(_remoteMock.Object);

        GetBuildCommandLineOptions options = new()
        {
            Id = buildId
        };

        GetBuildOperation getBuildOperation = new(options, _services);

        int result = await getBuildOperation.ExecuteAsync();

        result.Should().Be(Constants.ErrorCode);
    }
}
