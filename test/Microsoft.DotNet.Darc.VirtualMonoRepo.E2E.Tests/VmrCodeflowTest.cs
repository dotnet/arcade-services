// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Microsoft.DotNet.Darc.Tests.VirtualMonoRepo;

[TestFixture]
internal class VmrCodeflowTest : VmrTestsBase
{
    private const string FakePackageName = "Fake.Package";
    private const string FakePackageVersion = "1.0.0";

    private readonly string _productRepoFileName = Constants.GetRepoFileName(Constants.ProductRepoName);
    private readonly Mock<IBasicBarClient> _barClient = new();

    private NativePath _productRepoVmrPath = null!;
    private NativePath _productRepoVmrFilePath = null!;
    private NativePath _productRepoFilePath = null!;
    private NativePath _productRepoScriptFilePath = null!;

    protected override IServiceCollection CreateServiceProvider()
        => base.CreateServiceProvider()
            .AddSingleton(_barClient.Object);

    [SetUp]
    public void SetUp()
    {
        _productRepoVmrPath = VmrPath / VmrInfo.SourcesDir / Constants.ProductRepoName;
        _productRepoVmrFilePath = _productRepoVmrPath / _productRepoFileName;
        _productRepoScriptFilePath = ProductRepoPath / DarcLib.Constants.CommonScriptFilesPath / "build.ps1";
        _productRepoFilePath = ProductRepoPath / _productRepoFileName;
        _barClient.Reset();
    }

    [Test]
    public async Task OnlyBackflowsTest()
    {
        await EnsureTestRepoIsInitialized();

        const string branchName = nameof(OnlyBackflowsTest);

        var hadUpdates = await ChangeVmrFileAndFlowIt("New content from the VMR", branchName);
        hadUpdates.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(ProductRepoPath, branchName);

        // Backflow again - should be a no-op
        hadUpdates = await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        hadUpdates.ShouldNotHaveUpdates();

        // Make a change in the VMR again
        hadUpdates = await ChangeVmrFileAndFlowIt("New content from the VMR again", branchName);
        hadUpdates.ShouldHaveUpdates();

        // Make an additional change in the PR branch before merging
        await File.WriteAllTextAsync(_productRepoFilePath, "Change that happened in the PR");
        await GitOperations.CommitAll(ProductRepoPath, "Extra commit in the PR");
        await GitOperations.MergePrBranch(ProductRepoPath, branchName);

        // Make a conflicting change in the VMR
        hadUpdates = await ChangeVmrFileAndFlowIt("A completely different change", branchName);
        hadUpdates.ShouldHaveUpdates();
        await GitOperations.VerifyMergeConflict(ProductRepoPath, branchName,
            mergeTheirs: true,
            expectedFileInConflict: _productRepoFileName);

        // We used the changes from the VMR - let's verify flowing to the VMR
        hadUpdates = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        hadUpdates.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(VmrPath, branchName);
        CheckFileContents(_productRepoVmrFilePath, "A completely different change");
    }

    [Test]
    public async Task BackflowBuildsTest()
    {
        await EnsureTestRepoIsInitialized();

        const string branchName = nameof(BackflowBuildsTest);

        // Update a file in the VMR
        await File.WriteAllTextAsync(_productRepoVmrPath / _productRepoFileName, "Change that will have a build");

        // Update global.json in the VMR
        var updatedGlobalJson = await File.ReadAllTextAsync(VmrPath / VersionFiles.GlobalJson);
        await File.WriteAllTextAsync(VmrPath / VersionFiles.GlobalJson, updatedGlobalJson.Replace("9.0.100", "9.0.200"));

        // Update an eng/common file in the VMR
        Directory.CreateDirectory(VmrPath / DarcLib.Constants.CommonScriptFilesPath);
        await File.WriteAllTextAsync(VmrPath / DarcLib.Constants.CommonScriptFilesPath / "darc-init.ps1", "Some other script file");

        await GitOperations.CommitAll(VmrPath, "Changing a VMR's global.json and a file");

        // Pretend we have a build of the VMR
        const string newVersion = "1.2.0";
        var build = new Build(
            id: 4050,
            dateProduced: DateTimeOffset.Now,
            staleness: 0,
            released: false,
            stable: true,
            commit: await GitOperations.GetRepoLastCommit(VmrPath),
            channels: ImmutableList<Channel>.Empty,
            assets: new[]
            {
                new Asset(123, 4050, true, FakePackageName, newVersion, null),
                new Asset(124, 4050, true, DependencyFileManager.ArcadeSdkPackageName, newVersion, null),
            }.ToImmutableList(),
            dependencies: ImmutableList<BuildRef>.Empty,
            incoherencies: ImmutableList<BuildIncoherence>.Empty)
        {
            GitHubBranch = "main",
            GitHubRepository = VmrPath,
        };

        _barClient
            .Setup(x => x.GetBuildAsync(build.Id))
            .ReturnsAsync(build);

        var branch = await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath, branchName, buildToFlow: build.Id);
        branch.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(ProductRepoPath, branchName);

        List<NativePath> expectedFiles = [
            .. GetExpectedVersionFiles(ProductRepoPath),
            ProductRepoPath / DarcLib.Constants.CommonScriptFilesPath / "darc-init.ps1",
            _productRepoFilePath,
        ];

        CheckDirectoryContents(ProductRepoPath, expectedFiles);

        CheckFileContents(_productRepoFilePath, "Change that will have a build");

        // Verify that Version.Details.xml got updated with the new package "built" in the VMR
        Local local = GetLocal(ProductRepoPath);
        List<DependencyDetail> dependencies = await local.GetDependenciesAsync();

        dependencies.Should().Contain(dep =>
            dep.Name == FakePackageName
            && dep.RepoUri == build.GitHubRepository
            && dep.Commit == build.Commit
            && dep.Version == newVersion);

        dependencies.Should().Contain(dep =>
            dep.Name == DependencyFileManager.ArcadeSdkPackageName
            && dep.RepoUri == build.GitHubRepository
            && dep.Commit == build.Commit
            && dep.Version == newVersion);

        // Verify that global.json got updated
        DependencyFileManager dependencyFileManager = GetDependencyFileManager();
        JObject globalJson = await dependencyFileManager.ReadGlobalJsonAsync(ProductRepoPath, "main");
        JToken? arcadeVersion = globalJson.SelectToken($"msbuild-sdks.['{DependencyFileManager.ArcadeSdkPackageName}']", true);
        arcadeVersion?.ToString().Should().Be(newVersion);

        var dotnetVersion = await dependencyFileManager.ReadToolsDotnetVersionAsync(ProductRepoPath, "main");
        dotnetVersion.ToString().Should().Be("9.0.200");
    }

    [Test]
    public async Task OnlyForwardflowsTest()
    {
        await EnsureTestRepoIsInitialized();

        const string branchName = nameof(OnlyForwardflowsTest);

        var hadUpdates = await ChangeRepoFileAndFlowIt("New content in the individual repo", branchName);
        hadUpdates.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(VmrPath, branchName);

        // Flow again - should be a no-op
        hadUpdates = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        hadUpdates.ShouldNotHaveUpdates();

        // Make a change in the repo again
        hadUpdates = await ChangeRepoFileAndFlowIt("New content in the individual repo again", branchName);
        hadUpdates.ShouldHaveUpdates();

        // Make an additional change in the PR branch before merging
        await File.WriteAllTextAsync(_productRepoVmrFilePath, "Change that happened in the PR");
        await GitOperations.CommitAll(VmrPath, "Extra commit in the PR");
        await GitOperations.MergePrBranch(VmrPath, branchName);

        // Make a conflicting change in the VMR
        hadUpdates = await ChangeRepoFileAndFlowIt("A completely different change", branchName);
        hadUpdates.ShouldHaveUpdates();
        await GitOperations.VerifyMergeConflict(VmrPath, branchName,
            mergeTheirs: true,
            expectedFileInConflict: VmrInfo.SourcesDir / Constants.ProductRepoName / _productRepoFileName);

        // We used the changes from the repo - let's verify flowing back is a no-op
        hadUpdates = await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        hadUpdates.ShouldNotHaveUpdates();
    }

    [Test]
    public async Task ForwardflowBuildsTest()
    {
        await EnsureTestRepoIsInitialized();

        const string branchName = nameof(ForwardflowBuildsTest);

        var hadUpdates = await ChangeRepoFileAndFlowIt("New content in the individual repo", branchName);
        hadUpdates.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(VmrPath, branchName);

        // Flow again - should be a no-op
        hadUpdates = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        hadUpdates.ShouldNotHaveUpdates();

        // Update a file in the repo
        await File.WriteAllTextAsync(_productRepoFilePath, "Change that will have a build");

        // Update global.json in the repo
        var updatedGlobalJson = await File.ReadAllTextAsync(ProductRepoPath / VersionFiles.GlobalJson);
        await File.WriteAllTextAsync(ProductRepoPath / VersionFiles.GlobalJson, updatedGlobalJson.Replace("9.0.100", "9.0.200"));

        // Update an eng/common file in the repo
        Directory.CreateDirectory(ProductRepoPath / DarcLib.Constants.CommonScriptFilesPath);
        await File.WriteAllTextAsync(ProductRepoPath / DarcLib.Constants.CommonScriptFilesPath / "darc-init.ps1", "Some other script file");

        await GitOperations.CommitAll(ProductRepoPath, "Changing a VMR's global.json and a file");

        List<NativePath> expectedFiles =
        [
            .. GetExpectedVersionFiles(ProductRepoPath),
            ProductRepoPath / DarcLib.Constants.CommonScriptFilesPath / "darc-init.ps1",
            _productRepoScriptFilePath,
            _productRepoFilePath,
        ];

        CheckDirectoryContents(ProductRepoPath, expectedFiles);

        // Pretend we have a build of the repo
        const string newVersion = "1.2.0";
        var build = new Build(
            id: 4050,
            dateProduced: DateTimeOffset.Now,
            staleness: 0,
            released: false,
            stable: true,
            commit: await GitOperations.GetRepoLastCommit(ProductRepoPath),
            channels: ImmutableList<Channel>.Empty,
            assets: new[]
            {
                new Asset(123, 4050, true, FakePackageName, newVersion, null),
                new Asset(124, 4050, true, DependencyFileManager.ArcadeSdkPackageName, newVersion, null),
            }.ToImmutableList(),
            dependencies: ImmutableList<BuildRef>.Empty,
            incoherencies: ImmutableList<BuildIncoherence>.Empty)
        {
            GitHubBranch = "main",
            GitHubRepository = ProductRepoPath,
        };

        _barClient
            .Setup(x => x.GetBuildAsync(build.Id))
            .ReturnsAsync(build);

        hadUpdates = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName, buildToFlow: build.Id);
        hadUpdates.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(VmrPath, branchName);

        CheckFileContents(_productRepoVmrFilePath, "Change that will have a build");
    }

    [Test]
    public async Task ZigZagCodeflowTest()
    {
        const string aFileContent = "Added a new file in the VMR";
        const string bFileContent = "Added a new file in the product repo in the meantime";
        const string bFileContent2 = "New content for the b file";
        const string branchName = nameof(ZigZagCodeflowTest);

        await EnsureTestRepoIsInitialized();

        var hadUpdates = await ChangeRepoFileAndFlowIt("New content in the individual repo", branchName);
        await GitOperations.MergePrBranch(VmrPath, branchName);

        // Make some changes in the product repo
        await File.WriteAllTextAsync(ProductRepoPath / "a.txt", aFileContent);
        await File.WriteAllTextAsync(ProductRepoPath / "cloaked.dll", "A cloaked file");
        await GitOperations.CommitAll(ProductRepoPath, aFileContent);

        // Flow unrelated changes from the VMR
        hadUpdates = await ChangeVmrFileAndFlowIt("New content from the VMR", branchName);
        hadUpdates.ShouldHaveUpdates();

        // Before we merge the PR branch, make a change in the product repo
        await File.WriteAllTextAsync(ProductRepoPath / "b.txt", bFileContent);
        await GitOperations.CommitAll(ProductRepoPath, bFileContent);

        // Merge the backflow branch and verify files
        await GitOperations.MergePrBranch(ProductRepoPath, branchName);
        CheckFileContents(ProductRepoPath / "a.txt", aFileContent);
        CheckFileContents(ProductRepoPath / "b.txt", bFileContent);
        CheckFileContents(_productRepoFilePath, "New content from the VMR");

        // Make a change in the VMR again
        hadUpdates = await ChangeVmrFileAndFlowIt("New content from the VMR again", branchName);
        hadUpdates.ShouldHaveUpdates();

        // Make an additional change in the PR branch before merging
        await File.WriteAllTextAsync(_productRepoFilePath, "Change that happened in the PR");
        await GitOperations.CommitAll(ProductRepoPath, "Extra commit in the PR");
        await GitOperations.MergePrBranch(ProductRepoPath, branchName);

        // Forward flow
        await File.WriteAllTextAsync(ProductRepoPath / "b.txt", bFileContent2);
        await GitOperations.CommitAll(ProductRepoPath, bFileContent2);
        hadUpdates = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        hadUpdates.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(VmrPath, branchName);
        CheckFileContents(_productRepoVmrPath / "a.txt", aFileContent);
        CheckFileContents(_productRepoVmrPath / "b.txt", bFileContent2);
        CheckFileContents(_productRepoVmrFilePath, "Change that happened in the PR");
        File.Exists(_productRepoVmrPath / "cloaked.dll").Should().BeFalse();
        await GitOperations.CheckAllIsCommitted(VmrPath);
        await GitOperations.CheckAllIsCommitted(ProductRepoPath);

        // Backflow - should be a no-op
        hadUpdates = await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        hadUpdates.ShouldNotHaveUpdates();
    }

    [Test]
    public async Task SubmoduleCodeFlowTest()
    {
        await EnsureTestRepoIsInitialized();

        const string branchName = nameof(SubmoduleCodeFlowTest);

        var submodulePath = new UnixPath("externals/external-repo");
        await GitOperations.InitializeSubmodule(ProductRepoPath, "second-repo", SecondRepoPath, submodulePath);
        await GitOperations.CommitAll(ProductRepoPath, "Added a submodule");

        var _submoduleFileVmrPath = _productRepoVmrPath / submodulePath / Constants.GetRepoFileName(Constants.SecondRepoName);

        var branch = await ChangeVmrFileAndFlowIt("New content in the VMR repo", branchName);
        branch.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(ProductRepoPath, branchName);

        branch = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        branch.ShouldHaveUpdates();
        await GitOperations.CheckAllIsCommitted(VmrPath);
        await GitOperations.CheckAllIsCommitted(ProductRepoPath);
        await GitOperations.MergePrBranch(VmrPath, branchName);
        CheckFileContents(_submoduleFileVmrPath, "File in product-repo2");

        // Make an "invalid" change to the submodule in the VMR
        // This will be forbidden in the future but we need to test this
        await File.WriteAllLinesAsync(_submoduleFileVmrPath, new[] { "Invalid change" });
        await GitOperations.CommitAll(VmrPath, "Invalid change in the VMR");
        branch = await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        await GitOperations.CheckAllIsCommitted(VmrPath);
        await GitOperations.CheckAllIsCommitted(ProductRepoPath);
        branch.ShouldNotHaveUpdates();
    }

    // This one simulates what would happen if PR both ways are open and the one that was open later merges first.
    // The diagram it follows is here: https://github.com/dotnet/arcade/blob/prvysoky/backflow-design/Documentation/UnifiedBuild/images/parallel-merges.png
    [Test]
    public async Task OutOfOrderMergesTest()
    {
        await EnsureTestRepoIsInitialized();

        const string aFileContent = "Added a new file in the VMR";
        const string bFileContent = "Added a new file in the product repo in the meantime";
        const string backBranchName = nameof(OutOfOrderMergesTest);
        const string forwardBranchName = nameof(OutOfOrderMergesTest) + "-ff";

        // 1. Backflow PR + merge
        await File.WriteAllTextAsync(_productRepoVmrPath / "b.txt", bFileContent);
        await GitOperations.CommitAll(VmrPath, bFileContent);
        var backflowBranch = await ChangeVmrFileAndFlowIt("New content from the VMR", backBranchName);
        backflowBranch.ShouldHaveUpdates();
        await GitOperations.Checkout(ProductRepoPath, "main");

        // 3. Forward flow PR
        await File.WriteAllTextAsync(ProductRepoPath / "a.txt", aFileContent);
        await GitOperations.CommitAll(ProductRepoPath, aFileContent);
        var forwardFlowBranch = await ChangeRepoFileAndFlowIt("New content in the individual repo", forwardBranchName);
        forwardFlowBranch.ShouldHaveUpdates();
        await GitOperations.Checkout(VmrPath, "main");

        // 5. The backflow PR is now in conflict because it expects the original content but we have the one from step 3
        await GitOperations.VerifyMergeConflict(ProductRepoPath, backBranchName,
            mergeTheirs: true,
            expectedFileInConflict: _productRepoFileName);
        CheckFileContents(_productRepoFilePath, "New content from the VMR");

        // 7. The forward flow PR will have a conflict because it will expect the original content but we have the one from step 1
        await GitOperations.VerifyMergeConflict(VmrPath, forwardBranchName,
            mergeTheirs: true,
            expectedFileInConflict: VmrInfo.SourcesDir / Constants.ProductRepoName / _productRepoFileName);
        CheckFileContents(_productRepoVmrFilePath, "New content in the individual repo");

        // 10. Backflow again - technically
        await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath, branch: backBranchName);

        CheckFileContents(_productRepoFilePath, "New content in the individual repo");
        CheckFileContents(_productRepoVmrFilePath, "New content in the individual repo");
        CheckFileContents(_productRepoVmrPath / "a.txt", aFileContent);
        CheckFileContents(_productRepoVmrPath / "b.txt", bFileContent);
        CheckFileContents(ProductRepoPath / "a.txt", aFileContent);
        CheckFileContents(ProductRepoPath / "b.txt", bFileContent);
        await GitOperations.CheckAllIsCommitted(VmrPath);
        await GitOperations.CheckAllIsCommitted(ProductRepoPath);
    }

    // This repo simulates frequent changes in the Version.Details.xml file.
    // It tests how updates to different packages would (not) conflict with each other.
    [Test]
    public async Task VersionDetailsConflictTest()
    {
        const string branchName = nameof(VersionDetailsConflictTest);

        await EnsureTestRepoIsInitialized();

        await File.WriteAllTextAsync(ProductRepoPath / VersionFiles.VersionDetailsXml,
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Dependencies>
              <ProductDependencies>
                <!-- Dependencies from https://github.com/dotnet/repo1 -->
                <Dependency Name="Package.A1" Version="1.0.0">
                  <Uri>https://github.com/dotnet/repo1</Uri>
                  <Sha>a01</Sha>
                </Dependency>
                <Dependency Name="Package.B1" Version="1.0.0">
                  <Uri>https://github.com/dotnet/repo1</Uri>
                  <Sha>b02</Sha>
                </Dependency>
                <!-- End of dependencies from https://github.com/dotnet/repo1 -->
                <!-- Dependencies from https://github.com/dotnet/repo2 -->
                <Dependency Name="Package.C2" Version="1.0.0">
                  <Uri>https://github.com/dotnet/repo2</Uri>
                  <Sha>c03</Sha>
                </Dependency>
                <!-- End of dependencies from https://github.com/dotnet/repo2 -->
                <!-- Dependencies from https://github.com/dotnet/repo3 -->
                <Dependency Name="Package.D3" Version="1.0.0">
                  <Uri>https://github.com/dotnet/repo3</Uri>
                  <Sha>d04</Sha>
                </Dependency>
                <!-- End of dependencies from https://github.com/dotnet/repo3 -->
              </ProductDependencies>
              <ToolsetDependencies />
            </Dependencies>
            """);

        // The Versions.props file intentionally contains padding comment lines like in real repos
        // These lines make sure that neighboring lines are not getting in conflict when used as context during patch application
        // Repos like SDK have figured out that this is a good practice to avoid conflicts in the version files
        await File.WriteAllTextAsync(ProductRepoPath / VersionFiles.VersionProps,
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Project>
              <PropertyGroup>
                <MSBuildAllProjects>$(MSBuildAllProjects);$(MSBuildThisFileFullPath)</MSBuildAllProjects>
              </PropertyGroup>
              <PropertyGroup>
                <VersionPrefix>9.0.100</VersionPrefix>
              </PropertyGroup>
              <!-- Dependencies from https://github.com/dotnet/repo1 -->
              <PropertyGroup>
                <!-- Dependencies from https://github.com/dotnet/repo1-->
                <PackageA1PackageVersion>1.0.0</PackageA1PackageVersion>
                <PackageB1PackageVersion>1.0.0</PackageB1PackageVersion>
              </PropertyGroup>
              <!-- End of dependencies from https://github.com/dotnet/repo1 -->
              <!-- Dependencies from https://github.com/dotnet/repo2 -->
              <PropertyGroup>
                <!-- Dependencies from https://github.com/dotnet/repo2-->
                <PackageC2PackageVersion>1.0.0</PackageC2PackageVersion>
              </PropertyGroup>
              <!-- End of dependencies from https://github.com/dotnet/repo2 -->
              <!-- Dependencies from https://github.com/dotnet/repo3 -->
              <PropertyGroup>
                <!-- Dependencies from https://github.com/dotnet/repo3 -->
                <PackageD3PackageVersion>1.0.0</PackageD3PackageVersion>
              </PropertyGroup>
              <!-- End of dependencies from https://github.com/dotnet/repo3 -->
            </Project>
            """);

        // Level the repo and the VMR
        await GitOperations.CommitAll(ProductRepoPath, "Changing version files");
        var hadUpdates = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        hadUpdates.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(VmrPath, branchName);

        // Update repo1 and repo3 dependencies in the product repo
        await GetLocal(ProductRepoPath).UpdateDependenciesAsync(
            [
                new DependencyDetail
                {
                    Name = "Package.A1",
                    Version = "1.0.1",
                    RepoUri = "https://github.com/dotnet/repo1",
                    Commit = "abc",
                },
                new DependencyDetail
                {
                    Name = "Package.B1",
                    Version = "1.0.1",
                    RepoUri = "https://github.com/dotnet/repo1",
                    Commit = "abc",
                },
                new DependencyDetail
                {
                    Name = "Package.D3",
                    Version = "1.0.3",
                    RepoUri = "https://github.com/dotnet/repo3",
                    Commit = "def",
                },
            ],
            remoteFactory: null,
            ServiceProvider.GetRequiredService<IGitRepoFactory>(),
            Mock.Of<IBarApiClient>());

        await GitOperations.CommitAll(ProductRepoPath, "Update repo1 and repo3 dependencies in the product repo");

        var vmrVersionDetails = await File.ReadAllTextAsync(_productRepoVmrPath / VersionFiles.VersionDetailsXml);
        var vmrVersionProps = await File.ReadAllTextAsync(_productRepoVmrPath / VersionFiles.VersionProps);

        // Update repo2 dependencies in the VMR
        vmrVersionDetails = vmrVersionDetails
            .Replace(@"Package.C2"" Version=""1.0.0""", @"Package.C2"" Version=""2.0.0""")
            .Replace("<Sha>c03</Sha>", "<Sha>c04</Sha>");

        vmrVersionProps = vmrVersionProps
            .Replace("PackageC2PackageVersion>1.0.0", "PackageC2PackageVersion>2.0.0");

        await File.WriteAllTextAsync(_productRepoVmrPath / VersionFiles.VersionDetailsXml, vmrVersionDetails);
        await File.WriteAllTextAsync(_productRepoVmrPath / VersionFiles.VersionProps, vmrVersionProps);
        await GitOperations.CommitAll(VmrPath, "Update repo2 dependencies in the VMR");

        // Flow repo to the VMR
        hadUpdates = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName + "2");
        hadUpdates.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(VmrPath, branchName + "2");

        // Flow changes back from the VMR
        hadUpdates = await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath, branchName + "3");
        hadUpdates.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(ProductRepoPath, branchName + "3");

        // Verify the version files have both of the changes
        List<DependencyDetail> expectedDependencies =
        [
            new()
            {
                Name = "Package.A1",
                Version = "1.0.1",
                RepoUri = "https://github.com/dotnet/repo1",
                Commit = "abc",
                Type = DependencyType.Product,
            },
            new()
            {
                Name = "Package.B1",
                Version = "1.0.1",
                RepoUri = "https://github.com/dotnet/repo1",
                Commit = "abc",
                Type = DependencyType.Product,
            },
            new()
            {
                Name = "Package.C2",
                Version = "2.0.0",
                RepoUri = "https://github.com/dotnet/repo2",
                Commit = "c04",
                Type = DependencyType.Product,
            },
            new()
            {
                Name = "Package.D3",
                Version = "1.0.3",
                RepoUri = "https://github.com/dotnet/repo3",
                Commit = "def",
                Type = DependencyType.Product,
            },
        ];

        var dependencies = await GetLocal(ProductRepoPath)
            .GetDependenciesAsync();

        var vmrDependencies = new VersionDetailsParser()
            .ParseVersionDetailsFile(_productRepoVmrPath / VersionFiles.VersionDetailsXml)
            .Dependencies;

        dependencies.Should().BeEquivalentTo(expectedDependencies);
        vmrDependencies.Should().BeEquivalentTo(expectedDependencies);

        vmrVersionProps = await File.ReadAllTextAsync(_productRepoVmrPath / VersionFiles.VersionProps);
        CheckFileContents(ProductRepoPath / VersionFiles.VersionProps, expected: vmrVersionProps);
    }

    private async Task<bool> ChangeRepoFileAndFlowIt(string newContent, string branchName)
    {
        await File.WriteAllTextAsync(_productRepoFilePath, newContent);
        await GitOperations.CommitAll(ProductRepoPath, $"Changing a repo file to '{newContent}'");

        var hadUpdates = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        CheckFileContents(_productRepoVmrFilePath, newContent);
        await GitOperations.CheckAllIsCommitted(VmrPath);
        await GitOperations.CheckAllIsCommitted(ProductRepoPath);
        return hadUpdates;
    }

    private async Task<bool> ChangeVmrFileAndFlowIt(string newContent, string branchName)
    {
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

    private async Task EnsureTestRepoIsInitialized()
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
            Type = DependencyType.Product,
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
            Type = DependencyType.Product,
            Pinned = false,
        });

        await GitOperations.CommitAll(VmrPath, "Adding Arcade to the VMR");

        await InitializeRepoAtLastCommit(Constants.ProductRepoName, ProductRepoPath);

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
    }
}

static file class HelperExtensions
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

