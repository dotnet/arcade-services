// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using NUnit.Framework;
using NUnit.Framework.Internal;
using NUnit.Framework.Legacy;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using ProductConstructionService.ScenarioTests.Helpers;

namespace ProductConstructionService.ScenarioTests;

[TestFixture]
[Category("PostDeployment")]
[Category("GitHub")]
[Parallelizable]
internal class ScenarioTests_Dependencies : ScenarioTestBase
{
    [Test]
    public async Task ArcadeDependencies_EndToEnd()
    {
        var source1RepoName = TestRepository.TestRepo1Name;
        var source2RepoName = TestRepository.TestRepo3Name;
        var targetRepoName = TestRepository.TestRepo2Name;
        var target1BuildNumber = "098765";
        var target2BuildNumber = "987654";
        var sourceBuildNumber = "654321";
        var sourceCommit = "SourceCommitVar";
        var targetCommit = "TargetCommitVar";
        var sourceBranch = GetTestBranchName();
        var targetBranch = GetTestBranchName();
        var testChannelName = GetTestChannelName();

        List<AssetData> source1Assets = GetAssetData(GetUniqueAssetName("Foo"), "1.1.0", GetUniqueAssetName("Bar"), "2.1.0");
        List<AssetData> source2Assets = GetAssetData(GetUniqueAssetName("Pizza"), "3.1.0", GetUniqueAssetName("Hamburger"), "4.1.0");
        List<AssetData> targetAssets = GetAssetData(GetUniqueAssetName("Source1"), "3.1.0", GetUniqueAssetName("Source2"), "4.1.0");
        var source1RepoUri = GetGitHubRepoUrl(source1RepoName);
        var source2RepoUri = GetGitHubRepoUrl(source2RepoName);
        var targetRepoUri = GetGitHubRepoUrl(targetRepoName);

        TestContext.WriteLine($"Creating test channel {testChannelName}");
        await CreateTestChannelAsync(testChannelName);

        TestContext.WriteLine("Set up build1 for intake into target repository");
        Build build1 = await CreateBuildAsync(source1RepoUri, sourceBranch, sourceCommit, sourceBuildNumber, source1Assets);
        await AddBuildToChannelAsync(build1.Id, testChannelName);

        TestContext.WriteLine("Set up build2 for intake into target repository");
        Build build2 = await CreateBuildAsync(source2RepoUri, sourceBranch, sourceCommit, sourceBuildNumber, source2Assets);
        await AddBuildToChannelAsync(build2.Id, testChannelName);

        List<BuildRef> dependencies =
        [
            new BuildRef(build1.Id, true, 1),
            new BuildRef(build2.Id, true, 2)
        ];

        // Add the target build once, should populate the BuildDependencies table and calculate TimeToInclusion
        TestContext.WriteLine("Set up targetBuild in target repository");
        Build targetBuild1 = await CreateBuildAsync(targetRepoUri, targetBranch, targetCommit, target1BuildNumber, targetAssets, dependencies);
        await AddBuildToChannelAsync(targetBuild1.Id, testChannelName);
        var newTargetBuild1 = new Build(targetBuild1.Id, targetBuild1.DateProduced, targetBuild1.Staleness, targetBuild1.Released,
            targetBuild1.Stable, targetBuild1.Commit, targetBuild1.Channels, targetBuild1.Assets, dependencies, incoherencies: null);

        // Add the target build a second time, should populate the BuildDependencies table and use the previous TimeToInclusion
        Build targetBuild2 = await CreateBuildAsync(targetRepoUri, targetBranch, targetCommit, target2BuildNumber, targetAssets, dependencies);
        await AddBuildToChannelAsync(targetBuild2.Id, testChannelName);
        var newTargetBuild2 = new Build(targetBuild2.Id, targetBuild2.DateProduced, targetBuild2.Staleness, targetBuild2.Released,
            targetBuild2.Stable, targetBuild2.Commit, targetBuild2.Channels, targetBuild2.Assets, dependencies, incoherencies: null);

        Build retrievedBuild1 = await PcsApi.Builds.GetBuildAsync(targetBuild1.Id);
        Build retrievedBuild2 = await PcsApi.Builds.GetBuildAsync(targetBuild2.Id);

        retrievedBuild1.Dependencies.Should().HaveCount(newTargetBuild1.Dependencies.Count);
        retrievedBuild2.Dependencies.Should().HaveCount(newTargetBuild2.Dependencies.Count);

        var buildRefComparer = new BuildRefComparer();

        CollectionAssert.AreEqual(retrievedBuild1.Dependencies, newTargetBuild1.Dependencies, buildRefComparer);
        CollectionAssert.AreEqual(retrievedBuild2.Dependencies, newTargetBuild2.Dependencies, buildRefComparer);
    }
}
