// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using AwesomeAssertions;
using ProductConstructionService.Api.v2020_02_20.Models;
using Maestro.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Internal.Testing.DependencyInjection.Abstractions;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Moq;

using ProductConstructionService.Api.Api.v2020_02_20.Controllers;
using ProductConstructionService.DependencyFlow.WorkItems;
using ProductConstructionService.WorkItems;
using Commit = ProductConstructionService.Api.v2020_02_20.Models.Commit;

namespace ProductConstructionService.Api.Tests;

[TestFixture]
public partial class BuildController20200914Tests
{
    private const string Repository = "FAKE-REPOSITORY";
    private const string CommitHash = "FAKE-COMMIT";
    private const string CommitMessage = "FAKE-COMMIT-MESSAGE";
    private const string Account = "FAKE-ACCOUNT";
    private const string Project = "FAKE-PROJECT";
    private const string Branch = "FAKE-BRANCH";
    private const string BuildNumber = "20.9.18.20";

    [Test]
    public async Task CommitIsFound()
    {
        using TestData data = await TestData.Default.BuildAsync();

        int id;
        {
            IActionResult result = await data.Controller.Create(new BuildData
            {
                Commit = CommitHash,
                AzureDevOpsAccount = Account,
                AzureDevOpsProject = Project,
                AzureDevOpsRepository = Repository,
                AzureDevOpsBuildNumber = BuildNumber,
                AzureDevOpsBranch = Branch,
                GitHubBranch = Branch,
            });

            result.Should().BeAssignableTo<ObjectResult>();
            var objResult = (ObjectResult)result;
            objResult.StatusCode.Should().Be((int)HttpStatusCode.Created);
            objResult.Value.Should().BeAssignableTo<Build>();
            var build = (Build)objResult.Value!;

            id = build.Id;
            build.Commit.Should().Be(CommitHash);
            build.AzureDevOpsAccount.Should().Be(Account);
            build.AzureDevOpsProject.Should().Be(Project);
            build.AzureDevOpsBuildNumber.Should().Be(BuildNumber);
            build.AzureDevOpsRepository.Should().Be(Repository);
            build.AzureDevOpsBranch.Should().Be(Branch);
        }

        {
            var resultCommit = await data.Controller.GetCommit(id);
            var objResultCommit = (ObjectResult)resultCommit;
            objResultCommit.StatusCode.Should().Be((int)HttpStatusCode.OK);
            objResultCommit.Value.Should().BeAssignableTo<Commit>();
            var commit = (Commit)objResultCommit.Value!;

            commit.Message.Should().Be(CommitMessage);
            commit.Sha.Should().Be(CommitHash);
            commit.Author.Should().Be(Account);
        }
    }

    [TestDependencyInjectionSetup]
    private static class TestDataConfiguration
    {
        public static async Task Dependencies(IServiceCollection collection)
        {
            var connectionString = await SharedData.Database.GetConnectionString();
            collection.AddLogging(l => l.AddProvider(new NUnitLogger()));

            var mockIRemoteFactory = new Mock<IRemoteFactory>();
            var mockIRemote = new Mock<IRemote>();
            var mockWorkItemProducerFactory = new Mock<IWorkItemProducerFactory>();
            var mockWorkItemProducer = new Mock<IWorkItemProducer<BuildCoherencyInfoWorkItem>>();
            mockWorkItemProducerFactory.Setup(f => f.CreateProducer<BuildCoherencyInfoWorkItem>(false)).Returns(mockWorkItemProducer.Object);
            mockIRemoteFactory.Setup(f => f.CreateRemoteAsync(Repository)).ReturnsAsync(mockIRemote.Object);
            mockIRemote.Setup(f => f.GetCommitAsync(Repository, CommitHash)).ReturnsAsync(new Microsoft.DotNet.DarcLib.Commit(Account, CommitHash, CommitMessage));

            collection.AddSingleton(mockIRemote.Object);
            collection.AddSingleton(mockIRemoteFactory.Object);
            collection.AddSingleton(Mock.Of<IBasicBarClient>());
            collection.AddSingleton<IHostEnvironment>(new HostingEnvironment
            {
                EnvironmentName = Environments.Development
            });
            collection.AddBuildAssetRegistry(options =>
            {
                options.UseSqlServer(connectionString);
                options.EnableServiceProviderCaching(false);
            });
            collection.AddSingleton<ISystemClock, TestClock>();
            collection.AddSingleton(mockWorkItemProducerFactory.Object);
        }

        public static Func<IServiceProvider, BuildsController> Controller(IServiceCollection collection)
        {
            collection.AddTransient<BuildsController>();
            return s => s.GetRequiredService<BuildsController>();
        }
    }
}
