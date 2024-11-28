// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.Darc.VirtualMonoRepo.E2E.Tests;

[TestFixture]
internal class VmrTwoWayCodeflowTest : VmrCodeFlowTests
{
    [Test]
    public async Task ZigZagCodeflowTest()
    {
        const string aFileContent = "Added a new file in the repo";
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
        await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        await GitOperations.MergePrBranch(ProductRepoPath, branchName);
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
        await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        await GitOperations.MergePrBranch(ProductRepoPath, branchName);
        await GitOperations.CheckAllIsCommitted(VmrPath);
        await GitOperations.CheckAllIsCommitted(ProductRepoPath);
    }

    // This one simulates what would happen if PR both ways are open and the one that was open later merges first.
    // The diagram it follows is here (O are commits, x are conflicts):
    /*
        repo                   VMR
          O────────────────────►O   
          │  2.                 │   
          │   O◄────────────────O 1.
          │   │            4.   │   
        3.O───┼────────────►O   │   
          │   │             │   │   
          │ x─┘ 5.       7. x   │   
          │ │               │   │   
        6.O◄┘               └──►O 8.
          │                     │   
          |────────────────────►O 9.
          │                     │   
     */
    [Test]
    public async Task OutOfOrderMergesTest()
    {
        await EnsureTestRepoIsInitialized();

        const string aFileContent = "Added a new file in the repo";
        const string bFileContent = "Added a new file in the VMR";
        const string backBranchName = nameof(OutOfOrderMergesTest);
        const string forwardBranchName = nameof(OutOfOrderMergesTest) + "-ff";

        // 1. Change file in VMR
        // 2. Open a backflow PR
        await File.WriteAllTextAsync(_productRepoVmrPath / "b.txt", bFileContent);
        await GitOperations.CommitAll(VmrPath, bFileContent);
        var backflowBranch = await ChangeVmrFileAndFlowIt("New content from the VMR #1", backBranchName);
        backflowBranch.ShouldHaveUpdates();
        // We make another commit in the repo and add it to the PR branch (this is not in the diagram above)
        backflowBranch = await ChangeVmrFileAndFlowIt("New content from the VMR #2", backBranchName);
        backflowBranch.ShouldHaveUpdates();
        await GitOperations.Checkout(ProductRepoPath, "main");

        // 3. Change file in the repo
        // 4. Open a forward flow PR
        await File.WriteAllTextAsync(ProductRepoPath / "a.txt", aFileContent);
        await GitOperations.CommitAll(ProductRepoPath, aFileContent);
        var forwardFlowBranch = await ChangeRepoFileAndFlowIt("New content from the individual repo #1", forwardBranchName);
        forwardFlowBranch.ShouldHaveUpdates();
        // We make another commit in the repo and add it to the PR branch (this is not in the diagram above)
        forwardFlowBranch = await ChangeRepoFileAndFlowIt("New content from the individual repo #2", forwardBranchName);
        forwardFlowBranch.ShouldHaveUpdates();
        await GitOperations.Checkout(VmrPath, "main");

        // 5. The backflow PR is now in conflict - repo has the content from step 3 but VMR has the one from step 1
        // 6. We resolve the conflict by using the content from the VMR
        await GitOperations.VerifyMergeConflict(ProductRepoPath, backBranchName,
            mergeTheirs: true,
            expectedConflictingFile: _productRepoFileName);
        CheckFileContents(_productRepoFilePath, "New content from the VMR #2");

        // 7. The forward flow PR will have a conflict the opposite way - repo has the content from step 3 but VMR has the one from step 1
        // 8. We resolve the conflict by using the content from the VMR too
        await GitOperations.VerifyMergeConflict(VmrPath, forwardBranchName,
            mergeTheirs: true,
            expectedConflictingFile: VmrInfo.SourcesDir / Constants.ProductRepoName / _productRepoFileName);
        CheckFileContents(_productRepoVmrFilePath, "New content from the individual repo #2");

        // 9. We try to forward flow again so the VMR version of the file will flow back to the VMR
        // While the VMR accepted the content from the repo but it will get overriden by the VMR content again
        var hadUpdates = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath, branch: forwardBranchName);
        hadUpdates.ShouldHaveUpdates();
        await GitOperations.VerifyMergeConflict(VmrPath, forwardBranchName,
            mergeTheirs: true,
            expectedConflictingFile: VmrInfo.SourcesDir / Constants.ProductRepoName / _productRepoFileName);

        // Both VMR and repo need to have the version from the VMR as it flowed to the repo and back
        CheckFileContents(_productRepoFilePath, "New content from the VMR #2");
        CheckFileContents(_productRepoVmrFilePath, "New content from the VMR #2");
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
}

