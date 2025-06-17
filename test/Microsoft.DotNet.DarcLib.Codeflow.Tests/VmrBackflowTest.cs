// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Codeflow.Tests;

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
        // We want to flow the same build again, so the BarId doesn't change
        hadUpdates = await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath, branchName, useLatestBuild: true);
        await GitOperations.Checkout(ProductRepoPath, "main");
        await GitOperations.DeleteBranch(ProductRepoPath, branchName);
        CheckFileContents(_productRepoFilePath, "New content from the VMR");

        // Make a change in the VMR again
        hadUpdates = await ChangeVmrFileAndFlowIt("New content from the VMR again", branchName);
        hadUpdates.ShouldHaveUpdates();
        CheckFileContents(_productRepoFilePath, "New content from the VMR again");

        // Make an additional change in the PR branch before merging
        await GitOperations.Checkout(ProductRepoPath, branchName);
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

        var repo = GetLocal(ProductRepoPath);

        await repo.RemoveDependencyAsync(FakePackageName);

        await repo.AddDependencyAsync(new DependencyDetail
        {
            Name = "Package.A1",
            Version = "1.0.0",
            RepoUri = "https://github.com/dotnet/repo1",
            Commit = "a01",
            Type = DependencyType.Product,
        });

        await repo.AddDependencyAsync(new DependencyDetail
        {
            Name = "Package.B1",
            Version = "1.0.0",
            RepoUri = "https://github.com/dotnet/repo1",
            Commit = "b02",
            Type = DependencyType.Product,
        });

        await repo.AddDependencyAsync(new DependencyDetail
        {
            Name = "Package.C2",
            Version = "1.0.0",
            RepoUri = "https://github.com/dotnet/repo2",
            Commit = "c03",
            Type = DependencyType.Product,
        });

        await repo.AddDependencyAsync(new DependencyDetail
        {
            Name = "Package.D3",
            Version = "1.0.0",
            RepoUri = "https://github.com/dotnet/repo3",
            Commit = "d04",
            Type = DependencyType.Product,
        });

        await GitOperations.CommitAll(ProductRepoPath, "Set up version files");

        // Create global.json in src/arcade/ and in VMRs base
        Directory.CreateDirectory(ArcadeInVmrPath);
        await File.WriteAllTextAsync(
            ArcadeInVmrPath / VersionFiles.GlobalJson,
            Constants.GlobalJsonTemplate);
        await File.WriteAllTextAsync(
            VmrPath / VersionFiles.GlobalJson,
            Constants.VmrBaseGlobalJsonTemplate);
        await GitOperations.CommitAll(VmrPath, "Creating global.json in vmrs base and in src/arcade ");

        var hadUpdates = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        hadUpdates.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(VmrPath, branchName);

        // Update global.json in the VMR
        var updatedGlobalJson = await File.ReadAllTextAsync(ArcadeInVmrPath / VersionFiles.GlobalJson);
        await File.WriteAllTextAsync(ArcadeInVmrPath / VersionFiles.GlobalJson, updatedGlobalJson.Replace("9.0.100", "9.0.200"));

        // Update an eng/common file in the VMR
        Directory.CreateDirectory(ArcadeInVmrPath / DarcLib.Constants.CommonScriptFilesPath);
        await File.WriteAllTextAsync(ArcadeInVmrPath / DarcLib.Constants.CommonScriptFilesPath / "darc-init.ps1", "Some other script file");

        await GitOperations.CommitAll(VmrPath, "Changing a VMR arcade's global.json and eng/common file");

        var build1 = await CreateNewVmrBuild(
        [
            ("Package.A1", "1.0.1"),
            ("Package.B1", "1.0.1"),
            ("Package.C2", "2.0.0"),
            ("Package.D3", "1.0.3"),
            (DependencyFileManager.ArcadeSdkPackageName, "1.0.1"),
        ]);

        // Flow changes back from the VMR
        hadUpdates = await CallDarcBackflow(
            Constants.ProductRepoName,
            ProductRepoPath,
            branchName + "-backflow",
            buildToFlow: build1,
            excludedAssets: ["Package.C2"]);
        hadUpdates.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(ProductRepoPath, branchName + "-backflow");

        List<NativePath> expectedFiles = [
            .. GetExpectedVersionFiles(ProductRepoPath),
            ProductRepoPath / DarcLib.Constants.CommonScriptFilesPath / "darc-init.ps1",
            _productRepoFilePath,
        ];

        CheckDirectoryContents(ProductRepoPath, expectedFiles);

        // Verify the version files have both of the changes
        List<DependencyDetail> expectedDependencies =
        [
            ..GetDependencies(build1).Where(a => a.Name != "Package.C2"), // C2 is excluded
            new DependencyDetail
            {
                Name = "Package.C2",
                Version = "1.0.0",
                RepoUri = "https://github.com/dotnet/repo2",
                Commit = "c03",
                Type = DependencyType.Product,
                Pinned = false,
            },
        ];

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
            ("Package.A1", "1.0.2"),
            ("Package.B1", "1.0.2"),
            ("Package.C2", "2.0.2"),
            ("Package.D3", "1.0.2"),
            (DependencyFileManager.ArcadeSdkPackageName, "1.0.2"),
        ]);

        hadUpdates = await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath, branchName + "-pr", buildToFlow: build2);
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
                Name = "Package.D3",
                Version = "1.0.2", // Not part of the last 2 builds
                RepoUri = build2.GitHubRepository,
                Commit = build2.Commit,
                Type = DependencyType.Product,
                Pinned = false,
            },

            new DependencyDetail
            {
                Name = DependencyFileManager.ArcadeSdkPackageName,
                Version = "1.0.2",
                RepoUri = build2.GitHubRepository,
                Commit = build2.Commit,
                Type = DependencyType.Toolset,
                Pinned = false,
            },
        ];

        // We flow this latest build back into the PR that is waiting in the product repo
        hadUpdates = await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath, branchName + "-pr", buildToFlow: build3);
        hadUpdates.ShouldHaveUpdates();
        dependencies = await productRepo.GetDependenciesAsync();
        dependencies.Should().BeEquivalentTo(expectedDependencies);

        // Verify that global.json got updated
        DependencyFileManager dependencyFileManager = GetDependencyFileManager();
        JObject globalJson = await dependencyFileManager.ReadGlobalJsonAsync(ProductRepoPath, branchName + "-pr", repoIsVmr: false);
        JToken? arcadeVersion = globalJson.SelectToken($"msbuild-sdks.['{DependencyFileManager.ArcadeSdkPackageName}']", true);
        arcadeVersion?.ToString().Should().Be("1.0.2");

        var dotnetVersion = await dependencyFileManager.ReadToolsDotnetVersionAsync(ProductRepoPath, branchName + "-pr", repoIsVmr: false);
        dotnetVersion.ToString().Should().Be(Constants.VmrBaseDotnetSdkVersion);

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
        // Then we will change something in the VMR again but before we flow it back, we will make a conflicting change in the VMR
        await File.WriteAllTextAsync(_productRepoVmrFilePath, "New content again in the VMR #1");
        await GitOperations.CommitAll(VmrPath, "Changing a VMR file again #1");

        var build4 = await CreateNewVmrBuild(
        [
            ("Package.A1", "1.0.5"),
            ("Package.B1", "1.0.5"),
            ("Package.C2", "2.0.5"),
            ("Package.D3", "1.0.5"),
        ]);

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
        hadUpdates = await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath, branchName + "-pr2", buildToFlow: build4);
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
                Type = DependencyType.Toolset,
                Pinned = false,
            },
        ];
        dependencies = await productRepo.GetDependenciesAsync();
        dependencies.Should().BeEquivalentTo(expectedDependencies);

        // We make a conflicting change in the PR branch
        await File.WriteAllTextAsync(_productRepoFilePath, "New content again but this time in the PR directly");
        await GitOperations.CommitAll(ProductRepoPath, "Changing a repo file in the PR");

        // Flow the second build - this should throw as there's a conflict in the PR branch
        await this.Awaiting(_ => CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath, branchName + "-pr2", buildToFlow: build5))
            .Should().ThrowAsync<ConflictInPrBranchException>();

        // The state of the branch should be the same as before
        productRepo.Checkout(branchName + "-pr2");
        dependencies = await productRepo.GetDependenciesAsync();
        dependencies.Should().BeEquivalentTo(expectedDependencies);
        CheckFileContents(_productRepoFilePath, "New content again but this time in the PR directly");
    }

    /*
        This test verifies that we do not get conflicts in version files of follow-up backflow updates.
        Imagine a following scenario:

         repo                   VMR   
       1. O────────────────────►O────┐
          │                  2. │    O 3.
          │ 4.O◄────────────────┼────┘
          │   │                 │ 6.    
       5. O───┼────────────────►O────┐
          │   │                 │    O 7.
          │   x◄────────────────┼────┘
          │  8.                 │


        1. A commit is made in a repo. Doesn't matter what the change is.
        2. The commit is forward-flown into the VMR.
        3. The VMR builds commit from 2. and produces packages (such as Arcade.Sdk).
        4. A backflow PR is created in the repo updating Arcade.Sdk from 1.0.0 to 1.0.1.
        5. A new commit from the repo is made. Again, doesn't matter what the change is.
        6. The commit is forward-flown into the VMR.
        7. The VMR builds commit from 6. and produces packages 1.0.2.
        8. A backflow PR opened in 4. is now getting updated with changes from 7.

        The problem happens when we try to create 8:
        - The version files in step 4. are changed 1.0.0 -> 1.0.1.
        - The version files in step 8. need to be changed 1.0.1 -> 1.0.2.
        - We must make sure that there's no problem when updating 1.0.0 -> 1.0.1 -> 1.0.2.
          There could be one if patches were created from 1.0.0 -> 1.0.1 and 1.0.0 -> 1.0.2.
     */
    [Test]
    public async Task BackflowingSubsequentCommitsTest()
    {
        const string branchName = nameof(BackflowingDependenciesTest);

        await EnsureTestRepoIsInitialized();

        // Update an eng/common file in the VMR
        Directory.CreateDirectory(ArcadeInVmrPath / DarcLib.Constants.CommonScriptFilesPath);
        await File.WriteAllTextAsync(ArcadeInVmrPath / VersionFiles.GlobalJson, Constants.GlobalJsonTemplate);
        await File.WriteAllTextAsync(ArcadeInVmrPath / DarcLib.Constants.CommonScriptFilesPath / "darc-init.ps1", "Some other script file");
        await GitOperations.CommitAll(VmrPath, "Creating VMR's eng/common");

        // 1. A commit is made in a repo. Doesn't matter what the change is
        var repo = GetLocal(ProductRepoPath);
        await repo.RemoveDependencyAsync(FakePackageName);
        await GitOperations.CommitAll(ProductRepoPath, "Changing version files");

        // 2. The commit is forward-flown into the VMR
        await GitOperations.Checkout(ProductRepoPath, "main");
        var hadUpdates = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        hadUpdates.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(VmrPath, branchName);

        // 3. The VMR builds commit from 2. and produces packages
        var build1 = await CreateNewVmrBuild([(DependencyFileManager.ArcadeSdkPackageName, "1.0.1")]);
        var backflowBranch = branchName + "-backflow";

        // 4. A backflow PR is created in the repo updating Arcade.Sdk from 1.0.0 to 1.0.1
        await GitOperations.Checkout(VmrPath, "main");
        hadUpdates = await CallDarcBackflow(
            Constants.ProductRepoName,
            ProductRepoPath,
            backflowBranch,
            buildToFlow: build1);

        hadUpdates.ShouldHaveUpdates();

        // Verify the version files are updated
        var productRepo = GetLocal(ProductRepoPath);
        var dependencies = await productRepo.GetDependenciesAsync();
        dependencies.Should().BeEquivalentTo(GetDependencies(build1));

        // 5. A new commit from the repo is made. Again, doesn't matter what the change is
        // 6. The commit is forward-flown into the VMR
        var forwardFlowBranch = branchName + "-forwardflow";
        await GitOperations.Checkout(ProductRepoPath, "main");
        hadUpdates = await ChangeRepoFileAndFlowIt("New content in the repo", forwardFlowBranch);
        hadUpdates.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(VmrPath, forwardFlowBranch);

        // 7. The VMR builds commit from 6. and produces packages 1.0.2
        var build2 = await CreateNewVmrBuild([(DependencyFileManager.ArcadeSdkPackageName, "1.0.2")]);

        // 8. A backflow PR opened in 4. is now getting updated with changes from 7
        await GitOperations.Checkout(VmrPath, "main");
        hadUpdates = await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath, backflowBranch, buildToFlow: build2);
        hadUpdates.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(ProductRepoPath, backflowBranch);

        dependencies = await productRepo.GetDependenciesAsync();
        dependencies.Should().BeEquivalentTo(GetDependencies(build2));
    }

    [Test]
    public async Task BackflowingCorrectEngCommonTest()
    {
        const string branchName = nameof(BackflowingDependenciesTest);

        await EnsureTestRepoIsInitialized();

        var repo = GetLocal(ProductRepoPath);
        await repo.RemoveDependencyAsync(FakePackageName);
        await repo.AddDependencyAsync(new DependencyDetail
        {
            Name = "Package.A1",
            Version = "1.0.0",
            RepoUri = "https://github.com/dotnet/repo1",
            Commit = "a01",
            Type = DependencyType.Product,
        });

        await GitOperations.CommitAll(ProductRepoPath, "Changing version files");

        var hadUpdates = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        hadUpdates.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(VmrPath, branchName);

        var repoEngCommonFile = "a.txt";
        var vmrEngCommonFile = "b.txt";
        // create eng/common in the VMR in / and in /src/arcade
        Directory.CreateDirectory(VmrPath / DarcLib.Constants.CommonScriptFilesPath);
        await File.WriteAllTextAsync(VmrPath / DarcLib.Constants.CommonScriptFilesPath / repoEngCommonFile, "Not important");

        Directory.CreateDirectory(ArcadeInVmrPath / DarcLib.Constants.CommonScriptFilesPath);
        await File.WriteAllTextAsync(ArcadeInVmrPath / VersionFiles.GlobalJson, Constants.GlobalJsonTemplate);
        await File.WriteAllTextAsync(ArcadeInVmrPath / DarcLib.Constants.CommonScriptFilesPath / vmrEngCommonFile, "Some content");
        
        await GitOperations.CommitAll(VmrPath, "Creating test eng/commons");

        var build1 = await CreateNewVmrBuild(
        [
            (DependencyFileManager.ArcadeSdkPackageName, "1.0.1")
        ]);

        // Flow changes back from the VMR
        hadUpdates = await CallDarcBackflow(
            Constants.ProductRepoName,
            ProductRepoPath,
            branchName + "-backflow",
            buildToFlow: build1);
        hadUpdates.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(ProductRepoPath, branchName + "-backflow");

        // Verify that the product repo has the eng/common from src/arcade
        CheckFileContents(ProductRepoPath / DarcLib.Constants.CommonScriptFilesPath / vmrEngCommonFile, "Some content");
        File.Exists(ProductRepoPath / DarcLib.Constants.CommonScriptFilesPath / repoEngCommonFile).Should().BeFalse();
    }

    [Test]
    public async Task DarcVmrBackflowCommandTest()
    {
        await EnsureTestRepoIsInitialized();

        const string branchName = nameof(DarcVmrBackflowCommandTest);

        // We flow the repo to make sure they are in sync
        var hadUpdates = await ChangeVmrFileAndFlowIt("New content in the VMR", branchName);
        hadUpdates.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(ProductRepoPath, branchName);
        await GitOperations.Checkout(ProductRepoPath, "main");
        await GitOperations.Checkout(VmrPath, "main");

        hadUpdates = await ChangeRepoFileAndFlowIt("New content in the individual repo", branchName);
        hadUpdates.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(VmrPath, branchName);

        // Now we make several changes in the VMR and try to locally flow them via darc
        await File.WriteAllTextAsync(_productRepoVmrFilePath, "New content in the VMR again");
        await GitOperations.CommitAll(VmrPath, "New content in the VMR again");

        var options = new BackflowCommandLineOptions()
        {
            VmrPath = VmrPath,
            TmpPath = TmpPath,
            Repository = ProductRepoPath,
        };

        var operation = ActivatorUtilities.CreateInstance<BackflowOperation>(ServiceProvider, options);
        var currentDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(VmrPath);
        try
        {
            var result = await operation.ExecuteAsync();
            result.Should().Be(0);
        }
        finally
        {
            Directory.SetCurrentDirectory(currentDirectory);
        }

        // Verify that expected files are staged
        CheckFileContents(_productRepoFilePath, "New content in the VMR again");
        var processManager = ServiceProvider.GetRequiredService<IProcessManager>();

        var gitResult = await processManager.ExecuteGit(ProductRepoPath, "diff", "--name-only", "--cached");
        gitResult.Succeeded.Should().BeTrue("Git diff should succeed");
        var stagedFiles = gitResult.StandardOutput.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        stagedFiles.Should().BeEquivalentTo([_productRepoFileName], "There should be staged files after backflow");

        gitResult = await processManager.ExecuteGit(ProductRepoPath, "commit", "-m", "Commit staged files");
        await GitOperations.CheckAllIsCommitted(ProductRepoPath);
    }
}

