﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.Darc.VirtualMonoRepo.E2E.Tests;

internal abstract class VmrCodeFlowTests : VmrTestsBase
{
    protected const string FakePackageName = "Fake.Package";
    protected const string FakePackageVersion = "1.0.0";

    private int _buildId = 100;

    protected readonly string _productRepoFileName = Constants.GetRepoFileName(Constants.ProductRepoName);
    private readonly Mock<IBasicBarClient> _basicBarClient = new();

    protected NativePath _productRepoVmrPath = null!;
    protected NativePath _productRepoVmrFilePath = null!;
    protected NativePath _productRepoFilePath = null!;
    protected NativePath _productRepoScriptFilePath = null!;

    protected override IServiceCollection CreateServiceProvider()
        => base.CreateServiceProvider()
            .AddSingleton(_basicBarClient.Object);

    [SetUp]
    public void SetUp()
    {
        _productRepoVmrPath = VmrPath / VmrInfo.SourcesDir / Constants.ProductRepoName;
        _productRepoVmrFilePath = _productRepoVmrPath / _productRepoFileName;
        _productRepoScriptFilePath = ProductRepoPath / DarcLib.Constants.CommonScriptFilesPath / "build.ps1";
        _productRepoFilePath = ProductRepoPath / _productRepoFileName;
        _basicBarClient.Reset();
    }

    protected async Task EnsureTestRepoIsInitialized()
    {
        var vmrSha = await GitOperations.GetRepoLastCommit(VmrPath);

        // Add some eng/common content into the repo
        Directory.CreateDirectory(Path.GetDirectoryName(_productRepoScriptFilePath)!);
        await File.WriteAllTextAsync(_productRepoScriptFilePath, "Some common script file");
        await GitOperations.CommitAll(ProductRepoPath, "Add eng/common file into the repo");

        // We populate Version.Details.xml with a fake package which we will flow back and forth
        await GetLocal(ProductRepoPath).AddDependencyAsync(new DependencyDetail
        {
            Name = FakePackageName,
            Version = FakePackageVersion,
            RepoUri = VmrPath,
            Commit = vmrSha,
            Type = DependencyType.Product,
            Pinned = false,
        });

        await GitOperations.CommitAll(ProductRepoPath, "Adding a fake dependency");

        // We also add Arcade SDK so that we can verify eng/common updates
        await GetLocal(ProductRepoPath).AddDependencyAsync(new DependencyDetail
        {
            Name = DependencyFileManager.ArcadeSdkPackageName,
            Version = FakePackageVersion,
            RepoUri = VmrPath,
            Commit = vmrSha,
            Type = DependencyType.Toolset,
            Pinned = false,
        });

        await GitOperations.CommitAll(ProductRepoPath, "Adding Arcade dependency");

        // We also add Arcade SDK to VMR so that we can verify eng/common updates
        await GetLocal(VmrPath).AddDependencyAsync(new DependencyDetail
        {
            Name = DependencyFileManager.ArcadeSdkPackageName,
            Version = "1.0.0",
            RepoUri = VmrPath,
            Commit = vmrSha,
            Type = DependencyType.Toolset,
            Pinned = false,
        });

        await GitOperations.CommitAll(VmrPath, "Adding Arcade to the VMR");

        await InitializeRepoAtLastCommit(Constants.ProductRepoName, ProductRepoPath);
        await GitOperations.Checkout(ProductRepoPath, "main");

        var expectedFiles = GetExpectedFilesInVmr(
            VmrPath,
            [Constants.ProductRepoName],
            [_productRepoVmrFilePath, _productRepoVmrPath / DarcLib.Constants.CommonScriptFilesPath / "build.ps1"],
            hasVersionFiles: true);

        CheckDirectoryContents(VmrPath, expectedFiles);
        CompareFileContents(_productRepoVmrFilePath, _productRepoFileName);
        await GitOperations.CheckAllIsCommitted(VmrPath);

        await File.WriteAllTextAsync(ProductRepoPath / _productRepoFileName, "Test changes in repo file");
        await GitOperations.CommitAll(ProductRepoPath, "Changing a file in the repo");

        // Perform last VMR-lite-like forward flow
        await UpdateRepoToLastCommit(Constants.ProductRepoName, ProductRepoPath);

        CheckDirectoryContents(VmrPath, expectedFiles);
        CheckFileContents(_productRepoVmrFilePath, "Test changes in repo file");
        await GitOperations.CheckAllIsCommitted(VmrPath);
        await GitOperations.Checkout(VmrPath, "main");
        await GitOperations.Checkout(ProductRepoPath, "main");
    }

    protected async Task<Build> CreateNewVmrBuild((string name, string version)[] assets)
        => await CreateNewBuild(VmrPath, assets);

    protected async Task<Build> CreateNewRepoBuild((string name, string version)[] assets)
        => await CreateNewBuild(ProductRepoPath, assets);

    protected async Task<Build> CreateNewBuild(NativePath repoPath, (string name, string version)[] assets)
    {
        var assetId = 1;
        _buildId++;

        var build = new Build(
            id: _buildId,
            dateProduced: DateTimeOffset.Now,
            staleness: 0,
            released: false,
            stable: true,
            commit: await GitOperations.GetRepoLastCommit(repoPath),
            channels: ImmutableList<Channel>.Empty,
            assets:
            [
                ..assets.Select(a => new Asset(++assetId, _buildId, true, a.name, a.version,
                    [
                        new AssetLocation(assetId, LocationType.NugetFeed, "https://source.feed/index.json")
                    ]))
            ],
            dependencies: ImmutableList<BuildRef>.Empty,
            incoherencies: ImmutableList<BuildIncoherence>.Empty)
        {
            GitHubBranch = "main",
            GitHubRepository = repoPath,
        };

        _basicBarClient
            .Setup(x => x.GetBuildAsync(build.Id))
            .ReturnsAsync(build);

        return build;
    }

    protected static List<DependencyDetail> GetDependencies(Build build)
        => build.Assets
            .Select(a => new DependencyDetail
            {
                Name = a.Name,
                Version = a.Version,
                RepoUri = build.GitHubRepository,
                Commit = build.Commit,
                Type = DependencyType.Product,
                Pinned = false,
            })
            .ToList();

    protected async Task<bool> ChangeRepoFileAndFlowIt(string newContent, string branchName)
    {
        await GitOperations.Checkout(ProductRepoPath, "main");
        await File.WriteAllTextAsync(_productRepoFilePath, newContent);
        await GitOperations.CommitAll(ProductRepoPath, $"Changing a repo file to '{newContent}'");

        var hadUpdates = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        CheckFileContents(_productRepoVmrFilePath, newContent);
        await GitOperations.CheckAllIsCommitted(VmrPath);
        await GitOperations.CheckAllIsCommitted(ProductRepoPath);
        await GitOperations.Checkout(ProductRepoPath, "main");
        return hadUpdates;
    }

    protected async Task<bool> ChangeVmrFileAndFlowIt(string newContent, string branchName)
    {
        await GitOperations.Checkout(VmrPath, "main");
        await File.WriteAllTextAsync(_productRepoVmrPath / _productRepoFileName, newContent);
        await GitOperations.CommitAll(VmrPath, $"Changing a VMR file to '{newContent}'");

        var hadUpdates = await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        CheckFileContents(_productRepoFilePath, newContent);
        return hadUpdates;
    }

    protected override async Task CopyReposForCurrentTest()
    {
        CopyDirectory(VmrTestsOneTimeSetUp.TestsDirectory / Constants.SecondRepoName, SecondRepoPath);

        await CopyRepoAndCreateVersionFiles(Constants.ProductRepoName);
    }

    protected override async Task CopyVmrForCurrentTest()
    {
        await CopyRepoAndCreateVersionFiles("vmr");

        var sourceMappings = new SourceMappingFile()
        {
            Mappings =
            [
                new()
                {
                    Name = Constants.ProductRepoName,
                    DefaultRemote = ProductRepoPath,
                }
            ]
        };

        sourceMappings.Defaults.Exclude =
        [
            "externals/external-repo/**/*.exe",
            "excluded/*",
            "**/*.dll",
            "**/*.Dll",
        ];

        await WriteSourceMappingsInVmr(sourceMappings);
    }
}

internal static class BackFlowTestExtensions
{
    public static void ShouldHaveUpdates(this bool hadUpdates)
        => VerifyUpdates(hadUpdates, true, "new code flow updates are expected");

    public static void ShouldNotHaveUpdates(this bool hadUpdates)
        => VerifyUpdates(hadUpdates, false, "no updates are expected");

    private static void VerifyUpdates(bool hadUpdates, bool expected, string message)
    {
        hadUpdates.Should().Be(expected, message);
    }
}
