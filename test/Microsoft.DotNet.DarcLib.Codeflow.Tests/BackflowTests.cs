// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Codeflow.Tests;

[TestFixture]
internal class BackflowTests : CodeFlowTests
{
    [Test]
    public async Task OnlyBackflowsTest()
    {
        await EnsureTestRepoIsInitialized();

        const string branchName = nameof(OnlyBackflowsTest);

        var codeFlowResult = await ChangeVmrFileAndFlowIt("New content from the VMR", branchName);
        codeFlowResult.ShouldHaveUpdates();
        await FinalizeBackFlow(branchName);
        CheckFileContents(_productRepoFilePath, "New content from the VMR");

        // Backflow again - should be a no-op
        // We want to flow the same build again, so the BarId doesn't change
        codeFlowResult = await CallBackflow(Constants.ProductRepoName, ProductRepoPath, branchName, useLatestBuild: true);
        codeFlowResult.ShouldNotHaveUpdates();

        await GitOperations.Checkout(ProductRepoPath, "main");
        await GitOperations.DeleteBranch(ProductRepoPath, branchName);
        CheckFileContents(_productRepoFilePath, "New content from the VMR");

        // Make a change in the VMR again
        codeFlowResult = await ChangeVmrFileAndFlowIt("New content from the VMR again", branchName);
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.CommitAll(ProductRepoPath, "Backflow commit");
        CheckFileContents(_productRepoFilePath, "New content from the VMR again");

        // Make an additional change in the PR branch before merging
        await GitOperations.Checkout(ProductRepoPath, branchName);
        await File.WriteAllTextAsync(_productRepoFilePath, "Change that happened in the PR");
        await GitOperations.CommitAll(ProductRepoPath, "Extra commit in the PR");
        await FinalizeBackFlow(branchName);
        CheckFileContents(_productRepoFilePath, "Change that happened in the PR");

        // Make a conflicting change in the VMR
        codeFlowResult = await ChangeVmrFileAndFlowIt("A completely different change", branchName);
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.VerifyMergeConflict(ProductRepoPath, branchName,
            mergeTheirs: true,
            expectedConflictingFiles: [_productRepoFileName]);

        // We used the changes from the VMR - let's verify flowing to the VMR
        codeFlowResult = await CallForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        codeFlowResult.ShouldHaveUpdates();
        await FinalizeForwardFlow(branchName);
        CheckFileContents(_productRepoVmrFilePath, "A completely different change");

        // Now we will make a series of backflows where each will make a conflicting change
        // The last backflow will have to recreate all of the flows to be able to apply the changes

        // Make another flow to repo to have flows both ways ready
        codeFlowResult = await ChangeVmrFileAndFlowIt("Again some content from the VMR", branchName);
        codeFlowResult.ShouldHaveUpdates();
        await FinalizeBackFlow(branchName);

        // The file.txt will keep getting changed and conflicting in each flow
        await GitOperations.Checkout(ProductRepoPath, "main");
        await File.WriteAllTextAsync(ProductRepoPath / "file.txt", "Repo conflicting content");
        await GitOperations.CommitAll(ProductRepoPath, "Set up conflicting file in repo");

        for (int i = 1; i <= 3; i++)
        {
            await GitOperations.Checkout(VmrPath, "main");
            await File.WriteAllTextAsync(_productRepoVmrPath / "file.txt", $"VMR content {i}");
            await GitOperations.CommitAll(VmrPath, $"Add files for iteration {i}");
            codeFlowResult = await CallBackflow(Constants.ProductRepoName, ProductRepoPath, branchName);
            codeFlowResult.ShouldHaveUpdates();
            // Make a conflicting change in the PR branch before merging
            await File.WriteAllTextAsync(ProductRepoPath / $"conflicting_file_{i}.txt", $"Conflicting content {i}");
            await GitOperations.ExecuteGitCommand(ProductRepoPath, ["add", $"conflicting_file_{i}.txt"]);
            await GitOperations.VerifyMergeConflict(ProductRepoPath, branchName, ["file.txt"], mergeTheirs: false);
            CheckFileContents(ProductRepoPath / "file.txt", ["Repo conflicting content"]);
        }

        // Now we create a new backflow that will conflict with each of the previous flows
        await GitOperations.Checkout(VmrPath, "main");
        for (int i = 1; i <= 3; i++)
        {
            await File.WriteAllTextAsync(_productRepoVmrPath / $"file_{i}.txt", $"New content {i}");
            await File.WriteAllTextAsync(_productRepoVmrPath / $"conflicting_file_{i}.txt", $"New content {i}");
        }
        await GitOperations.CommitAll(VmrPath, "New conflicting flow");

        codeFlowResult = await CallBackflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.VerifyMergeConflict(
            ProductRepoPath,
            branchName,
            [
                ..Enumerable.Range(1, 3).Select(i => $"conflicting_file_{i}.txt"),
            ],
            mergeTheirs: true);

        for (int i = 1; i <= 3; i++)
        {
            CheckFileContents(ProductRepoPath / $"file_{i}.txt", $"New content {i}");
            CheckFileContents(ProductRepoPath / $"conflicting_file_{i}.txt", $"New content {i}");
        }
    }

    [Test]
    public async Task BackflowingDependenciesTest()
    {
        string backflowBranchName = GetTestBranchName();
        string forwardflowBranchName = GetTestBranchName(forwardFlow: true);

        await EnsureTestRepoIsInitialized();

        await AddDependencies(ProductRepoPath);

        // Create global.json in src/arcade/ and in VMRs base
        Directory.CreateDirectory(ArcadeInVmrPath);
        await File.WriteAllTextAsync(
            ArcadeInVmrPath / VersionFiles.GlobalJson,
            Constants.GlobalJsonTemplate);
        await File.WriteAllTextAsync(
            VmrPath / VersionFiles.GlobalJson,
            Constants.VmrBaseGlobalJsonTemplate);
        await GitOperations.CommitAll(VmrPath, "Creating global.json in vmrs base and in src/arcade ");

        var codeFlowResult = await CallForwardflow(Constants.ProductRepoName, ProductRepoPath, forwardflowBranchName);
        codeFlowResult.ShouldHaveUpdates();
        await FinalizeForwardFlow(forwardflowBranchName);

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
        codeFlowResult = await CallBackflow(
            Constants.ProductRepoName,
            ProductRepoPath,
            backflowBranchName,
            buildToFlow: build1,
            excludedAssets: ["Package.C2"]);
        codeFlowResult.ShouldHaveUpdates();
        await FinalizeBackFlow(backflowBranchName);

        List<NativePath> expectedFiles =
        [
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
        codeFlowResult = await CallForwardflow(Constants.ProductRepoName, ProductRepoPath, forwardflowBranchName);
        codeFlowResult.ShouldHaveUpdates();
        await FinalizeForwardFlow(forwardflowBranchName);

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

        codeFlowResult = await CallBackflow(Constants.ProductRepoName, ProductRepoPath, backflowBranchName, buildToFlow: build2);
        codeFlowResult.ShouldHaveUpdates();
        dependencies = await productRepo.GetDependenciesAsync();
        dependencies.Should().BeEquivalentTo(GetDependencies(build2));

        // Now we make an additional change in the PR to check it does not get overwritten with the following backflow
        await File.WriteAllTextAsync(_productRepoFilePath + "_2", "Change that happened in the PR");
        await GitOperations.CommitAll(ProductRepoPath, "Extra commit in the PR");

        // Then we flow another build into the VMR before merging the PR
        await GitOperations.Checkout(ProductRepoPath, "main");
        codeFlowResult = await ChangeRepoFileAndFlowIt("New content in the individual repo", forwardflowBranchName);
        codeFlowResult.ShouldHaveUpdates();
        await FinalizeForwardFlow(forwardflowBranchName);

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
        codeFlowResult = await CallBackflow(Constants.ProductRepoName, ProductRepoPath, backflowBranchName, buildToFlow: build3);
        codeFlowResult.ShouldHaveUpdates();
        dependencies = await productRepo.GetDependenciesAsync();
        dependencies.Should().BeEquivalentTo(expectedDependencies);

        // Verify that global.json got updated
        DependencyFileManager dependencyFileManager = GetDependencyFileManager();
        JObject globalJson = await dependencyFileManager.ReadGlobalJsonAsync(ProductRepoPath, backflowBranchName, relativeBasePath: null);
        JToken? arcadeVersion = globalJson.SelectToken($"msbuild-sdks.['{DependencyFileManager.ArcadeSdkPackageName}']", true);
        arcadeVersion?.ToString().Should().Be("1.0.2");

        var dotnetVersion = await dependencyFileManager.ReadToolsDotnetVersionAsync(ProductRepoPath, backflowBranchName, relativeBasePath: null);
        dotnetVersion.ToString().Should().Be(Constants.VmrBaseDotnetSdkVersion);

        await FinalizeBackFlow(backflowBranchName);

        expectedFiles.Add(new NativePath(_productRepoFilePath + "_2"));

        dependencies = await productRepo.GetDependenciesAsync();
        dependencies.Should().BeEquivalentTo(expectedDependencies);
        CheckFileContents(expectedFiles.Last(), "Change that happened in the PR");
        CheckFileContents(_productRepoVmrFilePath, "New content in the individual repo");
        CheckDirectoryContents(ProductRepoPath, expectedFiles);

        // Now we flow repo back to VMR to level the repos
        codeFlowResult = await CallForwardflow(Constants.ProductRepoName, ProductRepoPath, forwardflowBranchName);
        codeFlowResult.ShouldHaveUpdates();
        await FinalizeForwardFlow(forwardflowBranchName);

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
        codeFlowResult = await CallBackflow(Constants.ProductRepoName, ProductRepoPath, backflowBranchName, buildToFlow: build4);
        codeFlowResult.ShouldHaveUpdates();

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
        codeFlowResult = await CallBackflow(Constants.ProductRepoName, ProductRepoPath, backflowBranchName, buildToFlow: build5);
        codeFlowResult.HadConflicts.Should().BeTrue();
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
        var codeFlowResult = await CallForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        codeFlowResult.ShouldHaveUpdates();
        await FinalizeForwardFlow(branchName);

        // 3. The VMR builds commit from 2. and produces packages
        var build1 = await CreateNewVmrBuild([(DependencyFileManager.ArcadeSdkPackageName, "1.0.1")]);
        var backflowBranch = branchName + "-backflow";

        // 4. A backflow PR is created in the repo updating Arcade.Sdk from 1.0.0 to 1.0.1
        await GitOperations.Checkout(VmrPath, "main");
        codeFlowResult = await CallBackflow(
            Constants.ProductRepoName,
            ProductRepoPath,
            backflowBranch,
            buildToFlow: build1);

        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.CommitAll(ProductRepoPath, "Backflow commit");

        // Verify the version files are updated
        var productRepo = GetLocal(ProductRepoPath);
        var dependencies = await productRepo.GetDependenciesAsync();
        dependencies.Should().BeEquivalentTo(GetDependencies(build1));

        // 5. A new commit from the repo is made. Again, doesn't matter what the change is
        // 6. The commit is forward-flown into the VMR
        var forwardFlowBranch = branchName + "-forwardflow";
        await GitOperations.Checkout(ProductRepoPath, "main");
        codeFlowResult = await ChangeRepoFileAndFlowIt("New content in the repo", forwardFlowBranch);
        codeFlowResult.ShouldHaveUpdates();
        await FinalizeForwardFlow(forwardFlowBranch);

        // 7. The VMR builds commit from 6. and produces packages 1.0.2
        var build2 = await CreateNewVmrBuild([(DependencyFileManager.ArcadeSdkPackageName, "1.0.2")]);

        // 8. A backflow PR opened in 4. is now getting updated with changes from 7
        await GitOperations.Checkout(VmrPath, "main");
        codeFlowResult = await CallBackflow(Constants.ProductRepoName, ProductRepoPath, backflowBranch, buildToFlow: build2);
        codeFlowResult.ShouldHaveUpdates();
        await FinalizeBackFlow(backflowBranch);

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

        var codeFlowResult = await CallForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        codeFlowResult.ShouldHaveUpdates();
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
        codeFlowResult = await CallBackflow(
            Constants.ProductRepoName,
            ProductRepoPath,
            branchName + "-backflow",
            buildToFlow: build1);
        codeFlowResult.ShouldHaveUpdates();
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

        await File.WriteAllTextAsync(_productRepoVmrFilePath + "-removed-in-repo", "This file will be removed in the repo");
        await GitOperations.CommitAll(VmrPath, "Add a file that will be removed in the repo");

        // We flow the repo to make sure they are in sync
        var codeFlowResult = await ChangeVmrFileAndFlowIt("New content in the VMR", branchName);
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(ProductRepoPath, branchName);

        await AddDependencies(ProductRepoPath);

        await GitOperations.Checkout(VmrPath, "main");
        codeFlowResult = await ChangeRepoFileAndFlowIt("New content in the individual repo", branchName);
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(VmrPath, branchName);
        await GitOperations.Checkout(ProductRepoPath, "main");
        await GitOperations.Checkout(VmrPath, "main");

        File.Delete(_productRepoFilePath + "-removed-in-repo");
        await GitOperations.CommitAll(ProductRepoPath, "Remove a file that came from the VMR");

        // Now we make several changes in the VMR and try to locally flow them via darc
        await File.WriteAllTextAsync(_productRepoVmrFilePath, "New content in the VMR again");
        await File.WriteAllTextAsync(_productRepoVmrFilePath + "-added-in-repo", "New file from the VMR");

        // Update version files
        var newDependency = new DependencyDetail
        {
            Name = "Package.New",
            Version = "2.0.0",
            RepoUri = "https://github.com/some/repo",
            Commit = "commit-sha",
            Type = DependencyType.Toolset,
        };
        await GetLocal(VmrPath).AddDependencyAsync(
            newDependency,
            relativeBasePath: VmrInfo.GetRelativeRepoSourcesPath(Constants.ProductRepoName));

        await GitOperations.CommitAll(VmrPath, "New content in the VMR again");

        string[] stagedFiles = await CallDarcBackflow();

        // Verify that expected files are staged
        string[] expectedFiles =
        [
            _productRepoFileName,
            _productRepoFileName + "-added-in-repo",
            VersionFiles.VersionDetailsXml,
            VersionFiles.VersionDetailsProps,
        ];

        // We check if everything got staged properly
        stagedFiles.Should().BeEquivalentTo(expectedFiles, "There should be staged files after backflow");
        await Helpers.GitOperationsHelper.VerifyNoConflictMarkers(ProductRepoPath, stagedFiles);
        CheckFileContents(_productRepoFilePath, "New content in the VMR again");
        CheckFileContents(ProductRepoPath / expectedFiles[1], "New file from the VMR");
        File.Exists(ProductRepoPath / expectedFiles[0] + "-removed-in-repo").Should().BeFalse();
        File.Exists(ProductRepoPath / expectedFiles[2]).Should().BeTrue();

        // Now we reset, make a conflicting change and see if darc can handle it and the conflict appears
        await GitOperations.ExecuteGitCommand(ProductRepoPath, "reset", "--hard");
        await File.WriteAllTextAsync(_productRepoFilePath, "A conflicting change in the repo");
        await GitOperations.CommitAll(ProductRepoPath, "A conflicting change in the repo");

        // We change VMR's eng/common because that is what normally flows with the repo content in regular subscriptions
        await GitOperations.Checkout(VmrPath, "main");
        Directory.CreateDirectory(VmrPath / VmrInfo.GetRelativeRepoSourcesPath("arcade") / DarcLib.Constants.CommonScriptFilesPath);
        File.Copy(
            ProductRepoPath / DarcLib.Constants.CommonScriptFilesPath / "build.ps1",
            VmrPath / VmrInfo.GetRelativeRepoSourcesPath("arcade") / DarcLib.Constants.CommonScriptFilesPath / "build.ps2");

        await GitOperations.CommitAll(VmrPath, "Changing VMR's eng/common");

        Build build = await CreateNewVmrBuild(
        [
            ("Package.A1", "1.0.1"),
            ("Package.B1", "1.0.1"),
            ("Package.C2", "1.0.1"),
            ("Package.D3", "1.0.1"),
            (DependencyFileManager.ArcadeSdkPackageName, "1.0.1"),
        ]);

        expectedFiles = [
            ..expectedFiles,
            VersionFiles.GlobalJson,
            DarcLib.Constants.CommonScriptFilesPath + "/build.ps2",
        ];

        stagedFiles = await CallDarcBackflow(build.Id, [expectedFiles[0]]);
        stagedFiles.Should().BeEquivalentTo(expectedFiles, "There should be staged files after backflow");
        await Helpers.GitOperationsHelper.VerifyNoConflictMarkers(ProductRepoPath, stagedFiles.Except([expectedFiles[0]]));
        CheckFileContents(ProductRepoPath / expectedFiles[1], "New file from the VMR");
        File.Exists(ProductRepoPath / expectedFiles.Last().Replace("ps2", "ps1")).Should().BeFalse();

        // Now we commit this flow and verify all files are staged
        await GitOperations.ExecuteGitCommand(ProductRepoPath, ["checkout", "--theirs", "--", _productRepoFilePath]);
        await GitOperations.ExecuteGitCommand(ProductRepoPath, ["add", _productRepoFilePath]);
        await GitOperations.ExecuteGitCommand(ProductRepoPath, ["commit", "-m", "Committing the backflow"]);
        await GitOperations.CheckAllIsCommitted(ProductRepoPath);
        (await GetLocal(ProductRepoPath).GetDependenciesAsync(newDependency.Name)).Should().ContainEquivalentOf(newDependency);

        // Now we make another set of changes in the VMR and try again
        // This time it will be same direction flow as the previous one (before it was opposite)
        await GitOperations.Checkout(ProductRepoPath, "main");
        await GitOperations.Checkout(VmrPath, "main");

        File.Delete(_productRepoFilePath + "-added-in-repo");
        await GitOperations.CommitAll(ProductRepoPath, "Remove a file that was in the VMR");

        // Now we make several changes in the VMR and try to locally flow them via darc
        await File.WriteAllTextAsync(_productRepoVmrFilePath, "New content in the VMR AGAIN");
        await File.WriteAllTextAsync(_productRepoVmrFilePath + "-added-in-repo", "New file from the VMR AGAIN");
        newDependency = new DependencyDetail
        {
            Name = "Package.NewNew",
            Version = "3.0.0",
            RepoUri = "https://github.com/some/repo",
            Commit = "commit-sha",
            Type = DependencyType.Toolset,
        };
        await GetLocal(VmrPath).AddDependencyAsync(
            newDependency,
            relativeBasePath: VmrInfo.GetRelativeRepoSourcesPath(Constants.ProductRepoName));
        await GitOperations.CommitAll(VmrPath, "New content in the VMR again");

        build = await CreateNewVmrBuild(
        [
            ("Package.A1", "1.0.2"),
            ("Package.B1", "1.0.2"),
            ("Package.C2", "2.0.2"),
            ("Package.D3", "1.0.2"),
        ]);

        // File "-added-in-repo" is deleted in the repo and changed in the VMR so it will conflict
        stagedFiles = await CallDarcBackflow(build.Id, [expectedFiles[1]]);
        stagedFiles.Should().BeEquivalentTo(
        [
            expectedFiles[0],
            expectedFiles[1],
            VersionFiles.VersionDetailsXml,
            VersionFiles.VersionDetailsProps,
        ]);
        await Helpers.GitOperationsHelper.VerifyNoConflictMarkers(ProductRepoPath, stagedFiles.Except([expectedFiles[1]]));
        CheckFileContents(ProductRepoPath / expectedFiles[1], "New file from the VMR AGAIN");
        (await File.ReadAllTextAsync(ProductRepoPath / VersionFiles.VersionDetailsXml)).Should().Contain("1.0.2");
        (await File.ReadAllTextAsync(ProductRepoPath / VersionFiles.VersionDetailsProps)).Should().Contain("1.0.2");
        (await GetLocal(ProductRepoPath).GetDependenciesAsync("Package.B1")).First().Version.Should().Be("1.0.2");
        (await GetLocal(ProductRepoPath).GetDependenciesAsync(newDependency.Name)).Should().ContainEquivalentOf(newDependency);
    }

    // Tests a scenario where we misconfigure subscriptions and let two different VMR branches backflow into the same repo branch.
    // https://github.com/dotnet/arcade-services/issues/4973
    [Test]
    public async Task DoubleBackflowIsDetectedTest()
    {
        await EnsureTestRepoIsInitialized();

        const string branch1Name = nameof(DoubleBackflowIsDetectedTest);

        var codeFlowResult = await ChangeRepoFileAndFlowIt("New content in the individual repo", branch1Name);
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(VmrPath, branch1Name);

        // Create two commits in a diverging branches in the VMR
        var branch2Name = branch1Name + "-2";

        await GitOperations.CreateBranch(VmrPath, branch2Name);
        await File.WriteAllTextAsync(_productRepoVmrFilePath, "New content in the divergent");
        await GitOperations.CommitAll(VmrPath, "New content in the divergent");

        await GitOperations.Checkout(VmrPath, "main");
        await File.WriteAllTextAsync(_productRepoVmrFilePath, "New content in the main branch");
        await GitOperations.CommitAll(VmrPath, "New content in the main branch");

        // Now we try to backflow both branches into the same branch in the repo
        var backflowBranch = branch1Name + "-backflow";
        codeFlowResult = await CallBackflow(Constants.ProductRepoName, ProductRepoPath, backflowBranch);
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(ProductRepoPath, backflowBranch);

        await GitOperations.Checkout(VmrPath, branch2Name);
        var act = () => CallBackflow(Constants.ProductRepoName, ProductRepoPath, backflowBranch);
        await act.Should().ThrowAsync<NonLinearCodeflowException>("The backflow should fail as the target branch already has changes from another VMR branch");
    }

    // Test that the bug https://github.com/dotnet/arcade-services/issues/5331 doesn't happen
    [Test]
    public async Task TestBackflowDependencyDowngradesAfterCrossingFlow()
    {
        await EnsureTestRepoIsInitialized();

        const string ffBranchName = nameof(TestBackflowDependencyDowngradesAfterCrossingFlow) + "-ff";
        const string bfBranchName = nameof(TestBackflowDependencyDowngradesAfterCrossingFlow) + "-bf";

        var productRepo = GetLocal(ProductRepoPath);

        // Add a dependency to the product repo and flow it to the VMR
        await AddDependencies(ProductRepoPath);
        var codeFlowResult = await CallForwardflow(Constants.ProductRepoName, ProductRepoPath, ffBranchName);
        codeFlowResult.ShouldHaveUpdates();
        await FinalizeForwardFlow(ffBranchName);

        // Now update one of the dependencies, open a forward flow PR but don't merge it
        await GitOperations.Checkout(ProductRepoPath, "main");
        var dep = new DependencyDetail
        {
            Name = "Package.A1",
            Version = "1.0.1",
            RepoUri = "https://github.com/dotnet/repo1",
            Commit = "a011",
            Type = DependencyType.Product,
        };
        await productRepo.AddDependencyAsync(dep);
        await GitOperations.CommitAll(ProductRepoPath, "Updating Package.A1 to 1.0.1");
        codeFlowResult = await CallForwardflow(Constants.ProductRepoName, ProductRepoPath, ffBranchName);
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.CommitAll(VmrPath, "Forward flow commit");

        // Now update the same dependency again
        await GitOperations.Checkout(ProductRepoPath, "main");
        dep = new DependencyDetail
        {
            Name = "Package.A1",
            Version = "1.0.2",
            RepoUri = "https://github.com/dotnet/repo1",
            Commit = "a012",
            Type = DependencyType.Product,
        };
        await productRepo.AddDependencyAsync(dep);
        await GitOperations.CommitAll(ProductRepoPath, "Updating Package.A1 to 1.0.2");

        // Now open and merge a backflow
        codeFlowResult = await ChangeVmrFileAndFlowIt("New content in the VMR", bfBranchName);
        codeFlowResult.ShouldHaveUpdates();
        await FinalizeBackFlow(bfBranchName);

        // merge the forward flow PR
        await FinalizeForwardFlow(ffBranchName);

        // Open a backflow again, there shouldn't be any downgrades
        codeFlowResult = await ChangeVmrFileAndFlowIt("New content in the VMR again", bfBranchName);
        await FinalizeBackFlow(bfBranchName);
        codeFlowResult.DependencyUpdates.Should().BeEmpty();
    }

    private async Task AddDependencies(NativePath repoPath)
    {
        var repo = GetLocal(repoPath);

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

        await GitOperations.CommitAll(repoPath, "Set up version files");
    }

    /*
        This test verifies a scenario where a file is changed (added, removed, edited) and later reverted
        while there are unrelated conflicts at the same time.
        More details about this in https://github.com/dotnet/arcade-services/issues/5046

        It's the mirror of ForwardFlowWithRevertsAndConflictsTest.
    */
    [Test]
    public async Task BackflowWithRevertsAndConflictsTest()
    {
        string branchName = GetTestBranchName();

        await EnsureTestRepoIsInitialized();

        // Files that will cause a conflict later, each in a different way
        const string Conflict_FileRemovedInTargetAndChangedInSource = "CONFLICT_removed_in_target_and_changed_in_source.txt";
        const string Conflict_FileRemovedInSourceAndChangedInTarget = "CONFLICT_removed_in_source_and_changed_in_target.txt";
        const string Conflict_FileChangedInBoth = "CONFLICT_changed_in_both.txt";
        const string Conflict_FileAddedInBoth = "CONFLICT_added_in_both.txt";
        const string Conflict_FileRenamedInSource = "CONFLICT_renamed_in_source.txt";
        const string Conflict_FileRenamedInTarget = "CONFLICT_renamed_in_target.txt";
        const string Conflict_FileRenamedNewName = "file_renamed_in_source_new_name.txt";

        // Files which will be changed and later reverted in different ways
        const string Revert_FileAddedAndRemovedName = "REVERT_added_and_removed.txt";
        const string Revert_FileRemovedAndAddedName = "REVERT_removed_and_added.txt";
        const string Revert_FileChangedAndPartiallyRevertedName = "REVERT_changed_and_partially_reverted.txt";

        // Contents for the partially reverted file
        // We have initial content, then we add a line at the end,
        // Then we change a line in the middle + revert the addition of the line at the end.
        const string PartialRevertOriginal =
            """
            One
            Two
            Three
            Four
            Five
            Six
            Seven
            Eight
            Nine
            Ten
            """;

        const string PartialRevertChange1 =
            """
            One
            Two
            Three
            Four
            Five
            Six
            Seven
            Eight
            Nine
            Ten
            111111111111
            """;

        const string PartialRevertChange2 =
            """
            One
            22222222222
            Three
            Four
            Five
            Six
            Seven
            Eight
            Nine
            Ten
            """;

        const string OriginalFileRemovedAndAddedContent = "Original content that will be removed and re-added";

        await GitOperations.Checkout(ProductRepoPath, "main");
        await File.WriteAllTextAsync(ProductRepoPath / Revert_FileChangedAndPartiallyRevertedName, PartialRevertOriginal);
        await GitOperations.CommitAll(ProductRepoPath, "Set up file for partial revert");

        // Flow to VMR and back to populate the repo well (eng/common, the <Source /> tag..)

        // Prepare files that will conflict in different ways
        await File.WriteAllTextAsync(ProductRepoPath / Conflict_FileRemovedInTargetAndChangedInSource, "This file will be removed in target and changed in source");
        await File.WriteAllTextAsync(ProductRepoPath / Conflict_FileRemovedInSourceAndChangedInTarget, "This file will be removed in source and changed in target");
        await File.WriteAllTextAsync(ProductRepoPath / Conflict_FileChangedInBoth, "This file will be changed in both repos");
        await File.WriteAllTextAsync(ProductRepoPath / Conflict_FileRenamedInSource, "This file will be renamed in source");
        await File.WriteAllTextAsync(ProductRepoPath / Conflict_FileRenamedInTarget, "This file will be renamed in target");
        await GitOperations.CommitAll(ProductRepoPath, "Set up files for conflict test");

        var codeflowResult = await ChangeVmrFileAndFlowIt("Initial content", branchName);
        codeflowResult.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(ProductRepoPath, branchName);

        codeflowResult = await ChangeRepoFileAndFlowIt("Initial content in repo", branchName);
        codeflowResult.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(VmrPath, branchName);

        // Step 1: Make changes in VMR (source) - different ways we can change a file and revert that change later
        await File.WriteAllTextAsync(_productRepoVmrPath / Revert_FileRemovedAndAddedName, OriginalFileRemovedAndAddedContent);
        await File.WriteAllTextAsync(_productRepoVmrPath / Revert_FileAddedAndRemovedName, "This file will be added and then removed");
        await File.WriteAllTextAsync(_productRepoVmrPath / Revert_FileChangedAndPartiallyRevertedName, PartialRevertChange1);
        File.Delete(_productRepoVmrPath / Revert_FileRemovedAndAddedName);
        await GitOperations.CommitAll(VmrPath, "Make changes which will get reverted later", allowEmpty: false);

        var expectedStagedFiles = new List<string>
        {
            VersionFiles.VersionDetailsXml,
            Revert_FileAddedAndRemovedName,
            Revert_FileChangedAndPartiallyRevertedName,
        };

        // Step 2: Backflow first changes
        var stagedFiles = await CallDarcBackflow();
        stagedFiles.Should().BeEquivalentTo(expectedStagedFiles, "There should be staged files after backflow");
        await Helpers.GitOperationsHelper.VerifyNoConflictMarkers(ProductRepoPath, stagedFiles);
        CheckFileContents(ProductRepoPath / expectedStagedFiles[1], "This file will be added and then removed");
        CheckFileContents(ProductRepoPath / expectedStagedFiles[2], PartialRevertChange1);

        // Step 3: Make a conflicting change directly in the target (simulating a change in the PR branch)
        await GitOperations.CommitAll(ProductRepoPath, "Committing the backflow");
        await File.WriteAllTextAsync(ProductRepoPath / Conflict_FileRemovedInSourceAndChangedInTarget, "This file has been changed in target");
        await File.WriteAllTextAsync(ProductRepoPath / Conflict_FileChangedInBoth, "This file has been changed in target");
        await File.WriteAllTextAsync(ProductRepoPath / Conflict_FileAddedInBoth, "This file has been added in both repos (from repo)");
        File.Move(ProductRepoPath / Conflict_FileRenamedInTarget, ProductRepoPath / "file_renamed_in_target_new_name.txt");
        await File.WriteAllTextAsync(_productRepoFilePath, "This file will be changed the same way in both sides and not conflict");
        await GitOperations.CommitAll(ProductRepoPath, "Edit files directly in target (simulating PR branch change)", allowEmpty: false);

        // Step 4: Make reverts and conflict in VMR (source)
        File.Delete(_productRepoVmrPath / Conflict_FileRemovedInSourceAndChangedInTarget);
        await File.WriteAllTextAsync(_productRepoVmrPath / Conflict_FileChangedInBoth, "This file has been changed in source");
        await File.WriteAllTextAsync(_productRepoVmrPath / Conflict_FileAddedInBoth, "This file has been added in both repos (from vmr)");
        File.Move(_productRepoVmrPath / Conflict_FileRenamedInSource, _productRepoVmrPath / "file_renamed_in_source_new_name.txt");
        await File.WriteAllTextAsync(_productRepoVmrFilePath, "This file will be changed the same way in both sides and not conflict");
        await GitOperations.CommitAll(VmrPath, "Make conflicting changes", allowEmpty: false);

        await File.WriteAllTextAsync(_productRepoVmrPath / Revert_FileRemovedAndAddedName, OriginalFileRemovedAndAddedContent);
        await File.WriteAllTextAsync(_productRepoVmrPath / Revert_FileChangedAndPartiallyRevertedName, PartialRevertChange2);
        File.Delete(_productRepoVmrPath / Revert_FileAddedAndRemovedName);
        await GitOperations.CommitAll(VmrPath, "Revert changes", allowEmpty: false);

        expectedStagedFiles.Add(Revert_FileRemovedAndAddedName);
        expectedStagedFiles.Add(Conflict_FileRenamedNewName);
        expectedStagedFiles.Add(Conflict_FileAddedInBoth);
        expectedStagedFiles.Add(Conflict_FileRemovedInSourceAndChangedInTarget);
        expectedStagedFiles.Add(Conflict_FileChangedInBoth);

        string[] expectedConflictedFiles = [.. expectedStagedFiles.Skip(5)];

        // Step 5: Backflow with reverts and conflicts
        stagedFiles = await CallDarcBackflow(expectedConflicts: expectedConflictedFiles);

        stagedFiles.Should().BeEquivalentTo(expectedStagedFiles, "There should be staged files after backflow");

        await Helpers.GitOperationsHelper.VerifyNoConflictMarkers(
            ProductRepoPath,
            expectedStagedFiles
                .Except(expectedConflictedFiles)
                // File does not exist anymore
                .Except([Revert_FileAddedAndRemovedName]));

        // Resolve conflicts by taking the source VMR's changes
        foreach (var conflictedFile in expectedConflictedFiles)
        {
            await GitOperations.ExecuteGitCommand(ProductRepoPath, ["checkout", "--theirs", "--", conflictedFile]);
            await GitOperations.ExecuteGitCommand(ProductRepoPath, ["add", conflictedFile]);
        }

        await GitOperations.ExecuteGitCommand(ProductRepoPath, ["commit", "-m", "Committing the backflow"]);
        await GitOperations.CheckAllIsCommitted(ProductRepoPath);

        // Verify final state: The reverts should be correctly applied despite conflicts

        // FileAddedAndRemoved should not exist (was added then removed)
        File.Exists(ProductRepoPath / Revert_FileAddedAndRemovedName).Should().BeFalse(
            "File that was added and removed should not exist");

        // FileRemovedAndAdded should exist with original content (was removed and re-added)
        File.Exists(ProductRepoPath / Revert_FileRemovedAndAddedName).Should().BeTrue(
            "File that was removed and re-added should exist");
        (await File.ReadAllTextAsync(ProductRepoPath / Revert_FileRemovedAndAddedName)).Should().Be(
            OriginalFileRemovedAndAddedContent,
            "File should have its original content after revert");

        // FileChangedAndPartiallyReverted should have the second change
        File.Exists(ProductRepoPath / Revert_FileChangedAndPartiallyRevertedName).Should().BeTrue(
            "Partially reverted file should exist");

        (await File.ReadAllTextAsync(ProductRepoPath / Revert_FileChangedAndPartiallyRevertedName)).Should().Be(
            PartialRevertChange2,
            "Partially reverted file should have the second change");

        // Conflicted files should have the contents from the VMR (source)
        (await File.ReadAllTextAsync(ProductRepoPath / Conflict_FileAddedInBoth)).Should().Be(
            "This file has been added in both repos (from vmr)");

        (await File.ReadAllTextAsync(ProductRepoPath / Conflict_FileChangedInBoth)).Should().Be(
            "This file has been changed in source");

        (await File.ReadAllTextAsync(ProductRepoPath / Conflict_FileRenamedNewName)).Should().Be(
            "This file will be renamed in source");

        (await File.ReadAllTextAsync(ProductRepoPath / Conflict_FileRemovedInTargetAndChangedInSource)).Should().Be(
            "This file will be removed in target and changed in source");

        // Somehow git does not delete this file when we checkout --theirs even though it was deleted in the source
        // However, the conflict happens, which is important
        (await File.ReadAllTextAsync(ProductRepoPath / Conflict_FileRemovedInSourceAndChangedInTarget)).Should().Be(
            "This file has been changed in target");
    }
}
