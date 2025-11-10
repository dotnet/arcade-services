// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using NUnit.Framework;
using NUnit.Framework.Internal;
using ProductConstructionService.ScenarioTests.Helpers;

namespace ProductConstructionService.ScenarioTests;

[TestFixture]
[Category("PostDeployment")]
[Parallelizable]
internal class ScenarioTests_Builds : ScenarioTestBase
{
    private string _repoUrl;
    private readonly string _repoName = TestRepository.TestRepo1Name;
    private const string SourceBuildNumber = "654321";
    private const string SourceCommit = "123456";
    private const string SourceBranch = "master";

    private List<AssetData> _sourceAssets;

    [SetUp]
    public void SetUp()
    {
        _sourceAssets = GetAssetData(
           GetUniqueAssetName("Foo"), "1.1.0",
           GetUniqueAssetName("Bar"), "2.1.0");
    }

    // Create a new build and check some of the metadata. Then mark as released and check again
    [Test]
    public async Task ArcadeBuilds_EndToEnd()
    {
        TestContext.WriteLine("Darc/Maestro build-handling tests");
        using var scenarioDirectory = TemporaryDirectory.Get();

        _repoUrl = GetGitHubRepoUrl(_repoName);

        // Create a build for the source repo
        Build build = await CreateBuildAsync(_repoUrl, SourceBranch, SourceCommit, SourceBuildNumber, _sourceAssets);
        Build retrievedBuild = await PcsApi.Builds.GetBuildAsync(build.Id);
        retrievedBuild.Released.Should().BeFalse("Retrieved build has Released set to true when it should be false");

        // Release the build; gather-drop does not fetch anything unless the flag '--include-released' is included in its arguments (which the next operation does set)
        Build updatedBuild = await PcsApi.Builds.UpdateAsync(new BuildUpdate() { Released = true }, build.Id);
        updatedBuild.Released.Should().BeTrue("Retrieved build has Released set to false when it should be true");

        // Gather a drop with release included
        var gatherWithReleasedDir = Path.Combine(scenarioDirectory.Directory, "gather-with-released");
        var gatherDropOutput = "";

        TestContext.WriteLine("Starting 'Gather with released, where build has been set to released' using folder " + gatherWithReleasedDir);

        gatherDropOutput = await GatherDrop(build.Id, gatherWithReleasedDir, true, string.Empty);

        gatherDropOutput.Should().Contain($"Gathering drop for build {SourceBuildNumber}", "Gather with released 1");
        gatherDropOutput.Should().Contain($"Downloading asset {GetUniqueAssetName("Bar")}@2.1.0", "Gather with released 1");
        gatherDropOutput.Should().Contain($"Downloading asset {GetUniqueAssetName("Foo")}@1.1.0", "Gather with released 1");
        gatherDropOutput.Should().NotContain("always-download assets in build", "Gather with released 1");

        // Gather with release excluded (default behavior). Gather-drop should throw an error.
        TestContext.WriteLine("Starting 'Gather with release excluded' - gather-drop should throw an error.");

        var gatherWithNoReleasedDir = Path.Combine(scenarioDirectory.Directory, "gather-no-released");
        Assert.ThrowsAsync<ScenarioTestException>(async () => await GatherDrop(build.Id, gatherWithNoReleasedDir, false, string.Empty), "Gather with release excluded");

        // Unrelease the build
        Build unreleaseBuild = await PcsApi.Builds.UpdateAsync(new BuildUpdate() { Released = false }, build.Id);
        unreleaseBuild.Released.Should().BeFalse();

        // Gather with release excluded again (default behavior)
        var gatherWithNoReleased2Dir = Path.Combine(scenarioDirectory.Directory, "gather-no-released-2");
        TestContext.WriteLine("Starting 'Gather unreleased with release excluded' using folder " + gatherWithNoReleased2Dir);
        gatherDropOutput = await GatherDrop(build.Id, gatherWithNoReleased2Dir, false, string.Empty);

        gatherDropOutput.Should().Contain($"Gathering drop for build {SourceBuildNumber}", "Gather unreleased with release excluded");
        gatherDropOutput.Should().Contain($"Downloading asset {GetUniqueAssetName("Bar")}@2.1.0", "Gather unreleased with release excluded");
        gatherDropOutput.Should().Contain($"Downloading asset {GetUniqueAssetName("Foo")}@1.1.0", "Gather unreleased with release excluded");
        gatherDropOutput.Should().NotContain("always-download assets in build", "Gather unreleased with release excluded");

        // Gather with release excluded again, but specify --always-download-asset-filters
        var gatherWithNoReleased3Dir = Path.Combine(scenarioDirectory.Directory, "gather-no-released-3");
        TestContext.WriteLine("Starting 'Gather unreleased with release excluded' using folder " + gatherWithNoReleased3Dir);
        gatherDropOutput = await GatherDrop(build.Id, gatherWithNoReleased3Dir, false, GetUniqueAssetName("Bar").Replace(".", "\\.") + "$");

        gatherDropOutput.Should().Contain($"Gathering drop for build {SourceBuildNumber}", "Gather unreleased with release excluded");
        gatherDropOutput.Should().Contain($"Downloading asset {GetUniqueAssetName("Bar")}@2.1.0", "Gather unreleased with release excluded");
        gatherDropOutput.Should().Contain($"Downloading asset {GetUniqueAssetName("Foo")}@1.1.0", "Gather unreleased with release excluded");
        gatherDropOutput.Should().Contain("Found 1 always-download asset(s) in build", "Gather unreleased with release excluded");
    }
}
