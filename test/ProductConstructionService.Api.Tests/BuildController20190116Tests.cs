// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using FluentAssertions;
using ProductConstructionService.Api.v2019_01_16.Models;
using Maestro.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.Internal.Testing.DependencyInjection.Abstractions;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using ProductConstructionService.Api.Api.v2019_01_16.Controllers;

namespace ProductConstructionService.Api.Tests;

[TestFixture, NonParallelizable]
public partial class BuildController20190116Tests
{
    [Test]
    public async Task MinimalBuildIsCreatedAndCanRetrieved()
    {
        using TestData data = await TestData.Default.BuildAsync();

        var commitHash = "FAKE-COMMIT";
        var account = "FAKE-ACCOUNT";
        var project = "FAKE-PROJECT";
        var buildNumber = "20.5.19.20";
        var repository = "FAKE-REPOSITORY";
        var branch = "FAKE-BRANCH";

        int id;
        {
            IActionResult result = await data.Controller.Create(new BuildData
            {
                Commit = commitHash,
                AzureDevOpsAccount = account,
                AzureDevOpsProject = project,
                AzureDevOpsBuildNumber = buildNumber,
                AzureDevOpsRepository = repository,
                AzureDevOpsBranch = branch,
            });

            result.Should().BeAssignableTo<ObjectResult>();
            var objResult = (ObjectResult)result;
            objResult.StatusCode.Should().Be((int)HttpStatusCode.Created);
            objResult.Value.Should().BeAssignableTo<Build>();
            var build = (Build)objResult.Value!;

            id = build.Id;
            build.Commit.Should().Be(commitHash);
            build.AzureDevOpsAccount.Should().Be(account);
            build.AzureDevOpsProject.Should().Be(project);
            build.AzureDevOpsBuildNumber.Should().Be(buildNumber);
            build.AzureDevOpsRepository.Should().Be(repository);
            build.AzureDevOpsBranch.Should().Be(branch);
            build.DateProduced.Should().Be(data.Clock.UtcNow);
        }

        {
            var result = await data.Controller.GetBuild(id);
            result.Should().BeAssignableTo<ObjectResult>();
            var objResult = (ObjectResult)result;
            objResult.StatusCode.Should().Be((int)HttpStatusCode.OK);
            objResult.Value.Should().BeAssignableTo<Build>();
            var build = (Build)objResult.Value!;
            build.Commit.Should().Be(commitHash);
            build.AzureDevOpsAccount.Should().Be(account);
            build.AzureDevOpsProject.Should().Be(project);
            build.AzureDevOpsBuildNumber.Should().Be(buildNumber);
            build.AzureDevOpsRepository.Should().Be(repository);
            build.AzureDevOpsBranch.Should().Be(branch);
            build.DateProduced.Should().Be(data.Clock.UtcNow);
        }
    }

    [Test]
    public async Task NonsenseBuildIdReturnsNotFound()
    {
        using TestData data = await TestData.Default.BuildAsync();
        var result = await data.Controller.GetBuild(-99999);
        result.Should().BeAssignableTo<StatusCodeResult>();
        ((StatusCodeResult)result).StatusCode.Should().Be((int)HttpStatusCode.NotFound);
    }

    [Test]
    public async Task BuildWithDependenciesIsRegistered()
    {
        using TestData data = await TestData.Default.BuildAsync();

        var commitHash = "FAKE-COMMIT";
        var account = "FAKE-ACCOUNT";
        var project = "FAKE-PROJECT";
        var buildNumber = "20.5.19.20";
        var branch = "FAKE-BRANCH";

        Build aBuild;
        Build bBuild;
        {
            IActionResult result = await data.Controller.Create(new BuildData
            {
                Commit = commitHash,
                AzureDevOpsAccount = account,
                AzureDevOpsProject = project,
                AzureDevOpsBuildNumber = buildNumber + ".1",
                AzureDevOpsRepository = "A-REPO",
                AzureDevOpsBranch = branch,
            });
            aBuild = (Build)((ObjectResult)result).Value!;
        }
        data.Clock.UtcNow += TimeSpan.FromHours(1);
        {
            IActionResult result = await data.Controller.Create(new BuildData
            {
                Commit = commitHash,
                AzureDevOpsAccount = account,
                AzureDevOpsProject = project,
                AzureDevOpsBuildNumber = buildNumber + ".2",
                AzureDevOpsRepository = "B-REPO",
                AzureDevOpsBranch = branch,
            });
            bBuild = (Build)((ObjectResult)result).Value!;
        }
        data.Clock.UtcNow += TimeSpan.FromHours(1);
        Build cBuild;
        {
            int cBuildId;
            {
                IActionResult result = await data.Controller.Create(new BuildData
                {
                    Commit = commitHash,
                    AzureDevOpsAccount = account,
                    AzureDevOpsProject = project,
                    AzureDevOpsBuildNumber = buildNumber + ".3",
                    AzureDevOpsRepository = "C-REPO",
                    AzureDevOpsBranch = branch,
                    Dependencies =
                    [
                        new BuildRef(aBuild.Id, isProduct: true),
                        new BuildRef(bBuild.Id, isProduct: true),
                    ],
                });
                result.Should().BeAssignableTo<ObjectResult>();
                var objResult = (ObjectResult)result;
                objResult.StatusCode.Should().Be((int)HttpStatusCode.Created);
                objResult.Value.Should().BeAssignableTo<Build>();
                cBuild = (Build)objResult.Value!;
                cBuildId = cBuild.Id;
            }

            {
                IActionResult result = await data.Controller.GetBuild(cBuildId);
                result.Should().BeAssignableTo<ObjectResult>();
                var objResult = (ObjectResult)result;
                objResult.StatusCode.Should().Be((int)HttpStatusCode.OK);
                objResult.Value.Should().BeAssignableTo<Build>();
                cBuild = (Build)objResult.Value!;
            }

            cBuild.Dependencies.Should().HaveCount(2);
            cBuild.Dependencies.Should().Contain(b => b.BuildId == aBuild.Id);
            cBuild.Dependencies.Should().Contain(b => b.BuildId == bBuild.Id);
        }
    }

    [Test]
    public async Task BuildGraphIncludesOnlyRelatedBuilds()
    {
        using TestData data = await TestData.Default.BuildAsync();

        var commitHash = "FAKE-COMMIT";
        var account = "FAKE-ACCOUNT";
        var project = "FAKE-PROJECT";
        var buildNumber = "20.5.19.20";
        var branch = "FAKE-BRANCH";

        async Task<Build> CreateBuildAsync(string repo, string build, params Build[] dependencies)
        {
            var inputBuild = new BuildData
            {
                Commit = commitHash,
                AzureDevOpsAccount = account,
                AzureDevOpsProject = project,
                AzureDevOpsBuildNumber = buildNumber + "." + build,
                AzureDevOpsRepository = repo,
                AzureDevOpsBranch = branch,
                Dependencies = dependencies.Select(d => new BuildRef(d.Id, true)).ToList(),
            };

            return (Build)((ObjectResult)await data.Controller.Create(inputBuild)).Value!;
        }

        Build aBuild = await CreateBuildAsync("A-REPO", "1");
        Build bBuild = await CreateBuildAsync("B-REPO", "2");
        Build cBuild = await CreateBuildAsync("C-REPO", "3", aBuild, bBuild);
        await CreateBuildAsync("UNRELATED-REPO", "4");

        BuildGraph graph;
        {
            IActionResult result = await data.Controller.GetBuildGraph(cBuild.Id);
            result.Should().BeAssignableTo<ObjectResult>();
            var objResult = (ObjectResult)result;
            objResult.StatusCode.Should().Be((int)HttpStatusCode.OK);
            objResult.Value.Should().BeAssignableTo<BuildGraph>();
            graph = (BuildGraph)objResult.Value!;
        }

        graph.Builds.Should().HaveCount(3);
        graph.Builds.Should().ContainKey(aBuild.Id);
        graph.Builds.Should().ContainKey(bBuild.Id);
        graph.Builds.Should().ContainKey(cBuild.Id);
        graph.Builds[cBuild.Id].Dependencies.Select(r => r.BuildId).Should().Contain(aBuild.Id);
        graph.Builds[cBuild.Id].Dependencies.Select(r => r.BuildId).Should().Contain(bBuild.Id);
        graph.Builds[aBuild.Id].Dependencies.Should().BeEmpty();
        graph.Builds[bBuild.Id].Dependencies.Should().BeEmpty();
    }

    [TestDependencyInjectionSetup]
    private static class TestDataConfiguration
    {
        public static async Task Default(IServiceCollection collection)
        {
            var connectionString = await SharedData.Database.GetConnectionString();

            collection.AddLogging(l => l.AddProvider(new NUnitLogger()));
            collection.AddSingleton<IHostEnvironment>(new HostingEnvironment
            {
                EnvironmentName = Environments.Development
            });
            collection.AddBuildAssetRegistry(options =>
            {
                options.UseSqlServer(connectionString);
                options.EnableServiceProviderCaching(false);
            });
        }

        public static Func<IServiceProvider, TestClock> Clock(IServiceCollection collection)
        {
            collection.AddSingleton<ISystemClock, TestClock>();
            return s => (TestClock)s.GetRequiredService<ISystemClock>();
        }

        public static Func<IServiceProvider, BuildsController> Controller(IServiceCollection collection)
        {
            collection.AddTransient<BuildsController>();
            return s => s.GetRequiredService<BuildsController>();
        }
    }
}
