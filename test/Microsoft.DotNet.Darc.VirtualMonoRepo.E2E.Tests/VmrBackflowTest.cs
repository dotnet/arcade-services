// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Microsoft.DotNet.Darc.VirtualMonoRepo.E2E.Tests;

[TestFixture]
internal class VmrBackflowTest : VmrCodeFlowTests
{
    [Test]
    public async Task OnlyBackflowsTest()
    {
        await EnsureTestRepoIsInitialized();

        const string branchName = nameof(OnlyBackflowsTest);

        var hadUpdates = await ChangeVmrFileAndFlowIt("New content from the VMR", branchName);
        hadUpdates.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(ProductRepoPath, branchName);
        CheckFileContents(_productRepoFilePath, "New content from the VMR");

        // Backflow again - should be a no-op
        hadUpdates = await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        hadUpdates.ShouldNotHaveUpdates();
        await GitOperations.Checkout(ProductRepoPath, "main");
        await GitOperations.DeleteBranch(ProductRepoPath, branchName);
        CheckFileContents(_productRepoFilePath, "New content from the VMR");

        // Make a change in the VMR again
        hadUpdates = await ChangeVmrFileAndFlowIt("New content from the VMR again", branchName);
        hadUpdates.ShouldHaveUpdates();
        CheckFileContents(_productRepoFilePath, "New content from the VMR again");

        // Make an additional change in the PR branch before merging
        await File.WriteAllTextAsync(_productRepoFilePath, "Change that happened in the PR");
        await GitOperations.CommitAll(ProductRepoPath, "Extra commit in the PR");
        await GitOperations.MergePrBranch(ProductRepoPath, branchName);
        CheckFileContents(_productRepoFilePath, "Change that happened in the PR");

        // Make a conflicting change in the VMR
        hadUpdates = await ChangeVmrFileAndFlowIt("A completely different change", branchName);
        hadUpdates.ShouldHaveUpdates();
        await GitOperations.VerifyMergeConflict(ProductRepoPath, branchName,
            mergeTheirs: true,
            expectedConflictingFile: _productRepoFileName);

        // We used the changes from the VMR - let's verify flowing to the VMR
        hadUpdates = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        hadUpdates.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(VmrPath, branchName);
        CheckFileContents(_productRepoVmrFilePath, "A completely different change");
    }

    [Test]
    public async Task BackflowingDependenciesTest()
    {
        const string branchName = nameof(BackflowingDependenciesTest);

        await EnsureTestRepoIsInitialized();

        await File.WriteAllTextAsync(ProductRepoPath / VersionFiles.VersionDetailsXml,
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <Dependencies>
              <ProductDependencies>
                <!-- Dependencies from https://github.com/dotnet/arcade -->
                <Dependency Name="{DependencyFileManager.ArcadeSdkPackageName}" Version="1.0.0">
                  <Uri>https://github.com/dotnet/arcade</Uri>
                  <Sha>a01</Sha>
                </Dependency>
                <!-- End of dependencies from https://github.com/dotnet/arcade -->
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
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <Project>
              <PropertyGroup>
                <MSBuildAllProjects>$(MSBuildAllProjects);$(MSBuildThisFileFullPath)</MSBuildAllProjects>
              </PropertyGroup>
              <PropertyGroup>
                <VersionPrefix>9.0.100</VersionPrefix>
              </PropertyGroup>
              <!-- Dependencies from https://github.com/dotnet/arcade -->
              <PropertyGroup>
                <!-- Dependencies from https://github.com/dotnet/arcade-->
                <{VersionFiles.GetVersionPropsPackageVersionElementName(DependencyFileManager.ArcadeSdkPackageName)}>1.0.0</{VersionFiles.GetVersionPropsPackageVersionElementName(DependencyFileManager.ArcadeSdkPackageName)}>
              </PropertyGroup>
              <!-- End of dependencies from https://github.com/dotnet/arcade -->
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
        var repoSha = await GitOperations.GetRepoLastCommit(ProductRepoPath);

        var hadUpdates = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        hadUpdates.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(VmrPath, branchName);

        // Update global.json in the VMR
        var updatedGlobalJson = await File.ReadAllTextAsync(VmrPath / VersionFiles.GlobalJson);
        await File.WriteAllTextAsync(VmrPath / VersionFiles.GlobalJson, updatedGlobalJson.Replace("9.0.100", "9.0.200"));

        // Update an eng/common file in the VMR
        Directory.CreateDirectory(VmrPath / DarcLib.Constants.CommonScriptFilesPath);
        await File.WriteAllTextAsync(VmrPath / DarcLib.Constants.CommonScriptFilesPath / "darc-init.ps1", "Some other script file");

        await GitOperations.CommitAll(VmrPath, "Changing a VMR's global.json and eng/common file");

        var build1 = await CreateNewVmrBuild(
        [
            (DependencyFileManager.ArcadeSdkPackageName, "1.0.1"),
            ("Package.A1", "1.0.1"),
            ("Package.B1", "1.0.1"),
            ("Package.C2", "2.0.0"),
            ("Package.D3", "1.0.3"),
        ]);

        // Flow changes back from the VMR
        hadUpdates = await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath, branchName + "-backflow", buildToFlow: build1.Id);
        hadUpdates.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(ProductRepoPath, branchName + "-backflow");

        List<NativePath> expectedFiles = [
            .. GetExpectedVersionFiles(ProductRepoPath),
            ProductRepoPath / DarcLib.Constants.CommonScriptFilesPath / "darc-init.ps1",
            _productRepoFilePath,
        ];

        CheckDirectoryContents(ProductRepoPath, expectedFiles);

        // Verify the version files have both of the changes
        List<DependencyDetail> expectedDependencies = GetDependencies(build1);

        var productRepo = GetLocal(ProductRepoPath);

        var dependencies = await productRepo.GetDependenciesAsync();
        dependencies.Should().BeEquivalentTo(expectedDependencies);

        // Flow the changes (updated versions files only) to the VMR - both repos should have equal content at that point
        hadUpdates = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName + "-forwardflow");
        hadUpdates.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(VmrPath, branchName + "-forwardflow");

        NativePath versionDetailsPath = VmrPath / VmrInfo.SourcesDir / Constants.ProductRepoName / VersionFiles.VersionDetailsXml;
        var vmrVersionDetails = new VersionDetailsParser().ParseVersionDetailsFile(versionDetailsPath);
        vmrVersionDetails.Dependencies.Should().BeEquivalentTo(expectedDependencies);

        // Now we open a backflow PR with a new build
        var build2 = await CreateNewVmrBuild(
        [
            (DependencyFileManager.ArcadeSdkPackageName, "1.0.2"),
            ("Package.A1", "1.0.2"),
            ("Package.B1", "1.0.2"),
            ("Package.C2", "2.0.2"),
            ("Package.D3", "1.0.2"),
        ]);

        hadUpdates = await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath, branchName + "-pr", buildToFlow: build2.Id);
        hadUpdates.ShouldHaveUpdates();
        dependencies = await productRepo.GetDependenciesAsync();
        dependencies.Should().BeEquivalentTo(GetDependencies(build2));

        // Now we make an additional change in the PR to check it does not get overwritten with the following backflow
        await File.WriteAllTextAsync(_productRepoFilePath + "_2", "Change that happened in the PR");
        await GitOperations.CommitAll(ProductRepoPath, "Extra commit in the PR");

        // Then we flow another build into the VMR before merging the PR
        await GitOperations.Checkout(ProductRepoPath, "main");
        hadUpdates = await ChangeRepoFileAndFlowIt("New content in the individual repo", branchName + "-forwardflow");
        hadUpdates.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(VmrPath, branchName + "-forwardflow");

        var build3 = await CreateNewVmrBuild(
        [
            ("Package.A1", "1.0.3"),
            ("Package.B1", "1.0.3"),
            ("Package.C2", "2.0.3"),
            // We omit one package on purpose to test that the backflow will not overwrite the missing package
        ]);

        expectedDependencies =
        [
            ..GetDependencies(build3),

            new DependencyDetail
            {
                Name = DependencyFileManager.ArcadeSdkPackageName,
                Version = "1.0.2",
                RepoUri = build2.GitHubRepository,
                Commit = build2.Commit,
                Type = DependencyType.Product,
                Pinned = false,
            },

            new DependencyDetail
            {
                Name = "Package.D3",
                Version = "1.0.2", // Not part of the last 2 builds
                RepoUri = build2.GitHubRepository,
                Commit = build2.Commit,
                Type = DependencyType.Product,
                Pinned = false,
            }
        ];

        // We flow this latest build back into the PR that is waiting in the product repo
        hadUpdates = await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath, branchName + "-pr", buildToFlow: build3.Id);
        hadUpdates.ShouldHaveUpdates();
        dependencies = await productRepo.GetDependenciesAsync();
        dependencies.Should().BeEquivalentTo(expectedDependencies);

        // Verify that global.json got updated
        DependencyFileManager dependencyFileManager = GetDependencyFileManager();
        JObject globalJson = await dependencyFileManager.ReadGlobalJsonAsync(ProductRepoPath, "main");
        JToken? arcadeVersion = globalJson.SelectToken($"msbuild-sdks.['{DependencyFileManager.ArcadeSdkPackageName}']", true);
        arcadeVersion?.ToString().Should().Be("1.0.2");

        var dotnetVersion = await dependencyFileManager.ReadToolsDotnetVersionAsync(ProductRepoPath, "main");
        dotnetVersion.ToString().Should().Be("9.0.200");

        await GitOperations.MergePrBranch(ProductRepoPath, branchName + "-pr");

        expectedFiles.Add(new NativePath(_productRepoFilePath + "_2"));

        dependencies = await productRepo.GetDependenciesAsync();
        dependencies.Should().BeEquivalentTo(expectedDependencies);
        CheckFileContents(expectedFiles.Last(), "Change that happened in the PR");
        CheckFileContents(_productRepoVmrFilePath, "New content in the individual repo");
        CheckDirectoryContents(ProductRepoPath, expectedFiles);

        // Now we flow repo back to VMR to level the repos
        hadUpdates = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName + "-ff");
        hadUpdates.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(VmrPath, branchName + "-ff");

        // Now we will change something in the VMR and flow it back to the repo
        // Then we will change something in the VMR again but before we flow it back, we will update the repo package versions by running update-dependencies
        await File.WriteAllTextAsync(_productRepoVmrFilePath, "New content again in the VMR #1");
        await GitOperations.CommitAll(VmrPath, "Changing a VMR file again #1");

        var build4 = await CreateNewVmrBuild(
        [
            ("Package.A1", "1.0.5"),
            ("Package.B1", "1.0.5"),
            ("Package.C2", "2.0.5"),
            ("Package.D3", "1.0.5"),
        ]);

        // Now we will change something in the VMR and flow it back to the repo
        // Then we will change something in the VMR again but before we flow it back, we will change the PR branch so that there's a conflict
        await File.WriteAllTextAsync(_productRepoVmrFilePath, "New content again in the VMR #2");
        await GitOperations.CommitAll(VmrPath, "Changing a VMR file again #2");

        var build5 = await CreateNewVmrBuild(
        [
            (DependencyFileManager.ArcadeSdkPackageName, "1.0.6"),
            ("Package.A1", "1.0.6"),
            ("Package.B1", "1.0.6"),
            ("Package.C2", "2.0.6"),
            ("Package.D3", "1.0.6"),
        ]);

        // Flow the first build
        hadUpdates = await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath, branchName + "-pr2", buildToFlow: build4.Id);
        hadUpdates.ShouldHaveUpdates();

        expectedDependencies =
        [
            ..GetDependencies(build4),

            new DependencyDetail
            {
                Name = DependencyFileManager.ArcadeSdkPackageName,
                Version = "1.0.2",
                RepoUri = build2.GitHubRepository,
                Commit = build2.Commit,
                Type = DependencyType.Product,
                Pinned = false,
            },
        ];
        dependencies = await productRepo.GetDependenciesAsync();
        dependencies.Should().BeEquivalentTo(expectedDependencies);

        // We make a conflicting change in the PR branch
        // TODO

        // Flow the second build
        hadUpdates = await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath, branchName + "-pr2", buildToFlow: build5.Id);
        hadUpdates.ShouldHaveUpdates();
        dependencies = await productRepo.GetDependenciesAsync();
        dependencies.Should().BeEquivalentTo(GetDependencies(build5));
    }
}

