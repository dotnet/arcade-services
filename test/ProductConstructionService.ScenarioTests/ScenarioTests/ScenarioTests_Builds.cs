// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Shouldly;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using NUnit.Framework;
using NUnit.Framework.Internal;

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
        retrievedBuild.Released.ShouldBeFalse("Retrieved build has Released set to true when it should be false");

        // Release the build; gather-drop does not fetch anything unless the flag '--include-released' is included in its arguments (which the next operation does set)
        Build updatedBuild = await PcsApi.Builds.UpdateAsync(new BuildUpdate() { Released = true }, build.Id);
        updatedBuild.Released.ShouldBeTrue("Retrieved build has Released set to false when it should be true");

        // Gather a drop with release included
        var gatherWithReleasedDir = Path.Combine(scenarioDirectory.Directory, "gather-with-released");
        var gatherDropOutput = "";

        TestContext.WriteLine("Starting 'Gather with released, where build has been set to released' using folder " + gatherWithReleasedDir);

        gatherDropOutput = await GatherDrop(build.Id, gatherWithReleasedDir, true, string.Empty);

        gatherDropOutput.ShouldContain($"Gathering drop for build {SourceBuildNumber}", customMessage: "Gather with released 1");
        gatherDropOutput.ShouldContain($"Downloading asset {GetUniqueAssetName("Bar")}@2.1.0", customMessage: "Gather with released 1");
        gatherDropOutput.ShouldContain($"Downloading asset {GetUniqueAssetName("Foo")}@1.1.0", customMessage: "Gather with released 1");
        gatherDropOutput.ShouldNotContain("always-download assets in build", customMessage: "Gather with released 1");

        // Gather with release excluded (default behavior). Gather-drop should throw an error.
        TestContext.WriteLine("Starting 'Gather with release excluded' - gather-drop should throw an error.");

        var gatherWithNoReleasedDir = Path.Combine(scenarioDirectory.Directory, "gather-no-released");
        await Should.ThrowAsync<ScenarioTestException>(async () => await GatherDrop(build.Id, gatherWithNoReleasedDir, false, string.Empty), customMessage: "Gather with release excluded");

        // Unrelease the build
        Build unreleaseBuild = await PcsApi.Builds.UpdateAsync(new BuildUpdate() { Released = false }, build.Id);
        unreleaseBuild.Released.ShouldBeFalse();

        // Gather with release excluded again (default behavior)
        var gatherWithNoReleased2Dir = Path.Combine(scenarioDirectory.Directory, "gather-no-released-2");
        TestContext.WriteLine("Starting 'Gather unreleased with release excluded' using folder " + gatherWithNoReleased2Dir);
        gatherDropOutput = await GatherDrop(build.Id, gatherWithNoReleased2Dir, false, string.Empty);

        gatherDropOutput.ShouldContain($"Gathering drop for build {SourceBuildNumber}", customMessage: "Gather unreleased with release excluded");
        gatherDropOutput.ShouldContain($"Downloading asset {GetUniqueAssetName("Bar")}@2.1.0", customMessage: "Gather unreleased with release excluded");
        gatherDropOutput.ShouldContain($"Downloading asset {GetUniqueAssetName("Foo")}@1.1.0", customMessage: "Gather unreleased with release excluded");
        gatherDropOutput.ShouldNotContain("always-download assets in build", customMessage: "Gather unreleased with release excluded");

        // Gather with release excluded again, but specify --always-download-asset-filters
        var gatherWithNoReleased3Dir = Path.Combine(scenarioDirectory.Directory, "gather-no-released-3");
        TestContext.WriteLine("Starting 'Gather unreleased with release excluded' using folder " + gatherWithNoReleased3Dir);
        gatherDropOutput = await GatherDrop(build.Id, gatherWithNoReleased3Dir, false, GetUniqueAssetName("Bar").Replace(".", "\\.") + "$");

        gatherDropOutput.ShouldContain($"Gathering drop for build {SourceBuildNumber}", customMessage: "Gather unreleased with release excluded");
        gatherDropOutput.ShouldContain($"Downloading asset {GetUniqueAssetName("Bar")}@2.1.0", customMessage: "Gather unreleased with release excluded");
        gatherDropOutput.ShouldContain($"Downloading asset {GetUniqueAssetName("Foo")}@1.1.0", customMessage: "Gather unreleased with release excluded");
        gatherDropOutput.ShouldContain("Found 1 always-download asset(s) in build", customMessage: "Gather unreleased with release excluded");
    }
}
