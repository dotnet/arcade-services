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
internal class VmrCodeflowTest :  VmrTestsBase
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

        var branch = await ChangeVmrFileAndFlowIt("New content from the VMR");
        branch.Should().NotBeNull();
        await GitOperations.MergePrBranch(ProductRepoPath, branch!);

        // Backflow again - should be a no-op
        branch = await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath);
        branch.Should().BeNull();

        // Make a change in the VMR again
        branch = await ChangeVmrFileAndFlowIt("New content from the VMR again");
        branch.Should().NotBeNull();

        // Make an additional change in the PR branch before merging
        await File.WriteAllTextAsync(_productRepoFilePath, "Change that happened in the PR");
        await GitOperations.CommitAll(ProductRepoPath, "Extra commit in the PR");
        await GitOperations.MergePrBranch(ProductRepoPath, branch!);

        // Make a conflicting change in the VMR
        branch = await ChangeVmrFileAndFlowIt("A completely different change");
        branch.Should().NotBeNull();
        await GitOperations.VerifyMergeConflict(ProductRepoPath, branch!,
            mergeTheirs: true,
            expectedFileInConflict: _productRepoFileName);

        // We used the changes from the VMR - let's verify flowing to the VMR
        branch = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath);
        branch.Should().NotBeNull();
        await GitOperations.MergePrBranch(VmrPath, branch!);
        CheckFileContents(_productRepoVmrFilePath, "A completely different change");
    }

    [Test]
    public async Task BackflowBuildsTest()
    {
        await EnsureTestRepoIsInitialized();

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

        var branch = await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath, buildToFlow: build.Id);
        branch.Should().NotBeNull();
        await GitOperations.MergePrBranch(ProductRepoPath, branch!);

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

        var branch = await ChangeRepoFileAndFlowIt("New content in the individual repo");
        branch.Should().NotBeNull();
        await GitOperations.MergePrBranch(VmrPath, branch!);

        // Flow again - should be a no-op
        branch = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath);
        branch.Should().BeNull();

        // Make a change in the repo again
        branch = await ChangeRepoFileAndFlowIt("New content in the individual repo again");
        branch.Should().NotBeNull();

        // Make an additional change in the PR branch before merging
        await File.WriteAllTextAsync(_productRepoVmrFilePath, "Change that happened in the PR");
        await GitOperations.CommitAll(VmrPath, "Extra commit in the PR");
        await GitOperations.MergePrBranch(VmrPath, branch!);

        // Make a conflicting change in the VMR
        branch = await ChangeRepoFileAndFlowIt("A completely different change");
        branch.Should().NotBeNull();
        await GitOperations.VerifyMergeConflict(VmrPath, branch!,
            mergeTheirs: true,
            expectedFileInConflict: VmrInfo.SourcesDir / Constants.ProductRepoName / _productRepoFileName);

        // We used the changes from the repo - let's verify flowing back is a no-op
        branch = await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath);
        branch.Should().BeNull();
    }

    [Test]
    public async Task ForwardflowBuildsTest()
    {
        await EnsureTestRepoIsInitialized();

        var branch = await ChangeRepoFileAndFlowIt("New content in the individual repo");
        branch.Should().NotBeNull();
        await GitOperations.MergePrBranch(VmrPath, branch!);

        // Flow again - should be a no-op
        branch = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath);
        branch.Should().BeNull();

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

        branch = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath, buildToFlow: build.Id);
        branch.Should().NotBeNull();
        await GitOperations.MergePrBranch(VmrPath, branch!);

        CheckFileContents(_productRepoVmrFilePath, "Change that will have a build");
    }

    [Test]
    public async Task ZigZagCodeflowTest()
    {
        const string aFileContent = "Added a new file in the VMR";
        const string bFileContent = "Added a new file in the product repo in the meantime";
        const string bFileContent2 = "New content for the b file";

        await EnsureTestRepoIsInitialized();

        var branch = await ChangeRepoFileAndFlowIt("New content in the individual repo");
        await GitOperations.MergePrBranch(VmrPath, branch!);

        // Make some changes in the product repo
        await File.WriteAllTextAsync(ProductRepoPath / "a.txt", aFileContent);
        await File.WriteAllTextAsync(ProductRepoPath / "cloaked.dll", "A cloaked file");
        await GitOperations.CommitAll(ProductRepoPath, aFileContent);

        // Flow unrelated changes from the VMR
        branch = await ChangeVmrFileAndFlowIt("New content from the VMR");
        branch.Should().NotBeNull();

        // Before we merge the PR branch, make a change in the product repo
        await File.WriteAllTextAsync(ProductRepoPath / "b.txt", bFileContent);
        await GitOperations.CommitAll(ProductRepoPath, bFileContent);

        // Merge the backflow branch and verify files
        await GitOperations.MergePrBranch(ProductRepoPath, branch!);
        CheckFileContents(ProductRepoPath / "a.txt", aFileContent);
        CheckFileContents(ProductRepoPath / "b.txt", bFileContent);
        CheckFileContents(_productRepoFilePath, "New content from the VMR");

        // Make a change in the VMR again
        branch = await ChangeVmrFileAndFlowIt("New content from the VMR again");
        branch.Should().NotBeNull();

        // Make an additional change in the PR branch before merging
        await File.WriteAllTextAsync(_productRepoFilePath, "Change that happened in the PR");
        await GitOperations.CommitAll(ProductRepoPath, "Extra commit in the PR");
        await GitOperations.MergePrBranch(ProductRepoPath, branch!);

        // Forward flow
        await File.WriteAllTextAsync(ProductRepoPath / "b.txt", bFileContent2);
        await GitOperations.CommitAll(ProductRepoPath, bFileContent2);
        branch = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath);
        branch.Should().NotBeNull();
        await GitOperations.MergePrBranch(VmrPath, branch!);
        CheckFileContents(_productRepoVmrPath / "a.txt", aFileContent);
        CheckFileContents(_productRepoVmrPath / "b.txt", bFileContent2);
        CheckFileContents(_productRepoVmrFilePath, "Change that happened in the PR");
        File.Exists(_productRepoVmrPath / "cloaked.dll").Should().BeFalse();
        await GitOperations.CheckAllIsCommitted(VmrPath);
        await GitOperations.CheckAllIsCommitted(ProductRepoPath);

        // Backflow - should be a no-op
        branch = await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath);
        branch.Should().BeNull();
    }

    [Test]
    public async Task SubmoduleCodeFlowTest()
    {
        await EnsureTestRepoIsInitialized();

        var submodulePath = new UnixPath("externals/external-repo");
        await GitOperations.InitializeSubmodule(ProductRepoPath, "second-repo", SecondRepoPath, submodulePath);
        await GitOperations.CommitAll(ProductRepoPath, "Added a submodule");

        var _submoduleFileVmrPath = _productRepoVmrPath / submodulePath / Constants.GetRepoFileName(Constants.SecondRepoName);

        var branch = await ChangeVmrFileAndFlowIt("New content in the VMR repo");
        branch.Should().NotBeNull();
        await GitOperations.MergePrBranch(ProductRepoPath, branch!);

        branch = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath);
        branch.Should().NotBeNull();
        await GitOperations.CheckAllIsCommitted(VmrPath);
        await GitOperations.CheckAllIsCommitted(ProductRepoPath);
        await GitOperations.MergePrBranch(VmrPath, branch!);
        CheckFileContents(_submoduleFileVmrPath, "File in product-repo2");

        // Make an "invalid" change to the submodule in the VMR
        // This will be forbidden in the future but we need to test this
        await File.WriteAllLinesAsync(_submoduleFileVmrPath, new[] { "Invalid change" });
        await GitOperations.CommitAll(VmrPath, "Invalid change in the VMR");
        branch = await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath);
        await GitOperations.CheckAllIsCommitted(VmrPath);
        await GitOperations.CheckAllIsCommitted(ProductRepoPath);
        branch.Should().BeNull();
    }

    // This one simulates what would happen if PR both ways are open and the one that was open later merges first.
    // The diagram it follows is here: https://github.com/dotnet/arcade/blob/prvysoky/backflow-design/Documentation/UnifiedBuild/images/parallel-merges.png
    [Test]
    public async Task OutOfOrderMergesTest()
    {
        await EnsureTestRepoIsInitialized();

        const string aFileContent = "Added a new file in the VMR";
        const string bFileContent = "Added a new file in the product repo in the meantime";

        // 1. Backflow PR + merge
        await File.WriteAllTextAsync(_productRepoVmrPath / "b.txt", bFileContent);
        await GitOperations.CommitAll(VmrPath, bFileContent);
        var backflowBranch = await ChangeVmrFileAndFlowIt("New content from the VMR");
        backflowBranch.Should().NotBeNull();
        await GitOperations.Checkout(ProductRepoPath, "main");

        // 3. Forward flow PR
        await File.WriteAllTextAsync(ProductRepoPath / "a.txt", aFileContent);
        await GitOperations.CommitAll(ProductRepoPath, aFileContent);
        var forwardFlowBranch = await ChangeRepoFileAndFlowIt("New content in the individual repo");
        forwardFlowBranch.Should().NotBeNull();
        await GitOperations.Checkout(VmrPath, "main");

        // 5. The backflow PR is now in conflict because it expects the original content but we have the one from step 3
        await GitOperations.VerifyMergeConflict(ProductRepoPath, backflowBranch!,
            mergeTheirs: true,
            expectedFileInConflict: _productRepoFileName);
        CheckFileContents(_productRepoFilePath, "New content from the VMR");

        // 7. The forward flow PR will have a conflict because it will expect the original content but we have the one from step 1
        await GitOperations.VerifyMergeConflict(VmrPath, forwardFlowBranch!,
            mergeTheirs: true,
            expectedFileInConflict: VmrInfo.SourcesDir / Constants.ProductRepoName / _productRepoFileName);
        CheckFileContents(_productRepoVmrFilePath, "New content in the individual repo");

        // 10. Backflow again - technically
        await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath);

        CheckFileContents(_productRepoFilePath, "New content in the individual repo");
        CheckFileContents(_productRepoVmrFilePath, "New content in the individual repo");
        CheckFileContents(_productRepoVmrPath / "a.txt", aFileContent);
        CheckFileContents(_productRepoVmrPath / "b.txt", bFileContent);
        CheckFileContents(ProductRepoPath / "a.txt", aFileContent);
        CheckFileContents(ProductRepoPath / "b.txt", bFileContent);
        await GitOperations.CheckAllIsCommitted(VmrPath);
        await GitOperations.CheckAllIsCommitted(ProductRepoPath);
    }

    private async Task<string?> ChangeRepoFileAndFlowIt(string newContent)
    {
        await File.WriteAllTextAsync(_productRepoFilePath, newContent);
        await GitOperations.CommitAll(ProductRepoPath, $"Changing a repo file to '{newContent}'");

        var branch = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath);
        CheckFileContents(_productRepoVmrFilePath, newContent);
        await GitOperations.CheckAllIsCommitted(VmrPath);
        await GitOperations.CheckAllIsCommitted(ProductRepoPath);
        return branch;
    }

    private async Task<string?> ChangeVmrFileAndFlowIt(string newContent)
    {
        await File.WriteAllTextAsync(_productRepoVmrPath / _productRepoFileName, newContent);
        await GitOperations.CommitAll(VmrPath, $"Changing a VMR file to '{newContent}'");

        var branch = await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath);
        CheckFileContents(_productRepoFilePath, newContent);
        return branch;
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

