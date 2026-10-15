// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using NUnit.Framework;
using NUnit.Framework.Internal;

namespace ProductConstructionService.ScenarioTests;

[TestFixture]
[Category("PostDeployment")]
[Parallelizable]
internal class ScenarioTests_RepoPolicies : ScenarioTestBase
{
    private readonly string _repoName = TestRepository.TestRepo1Name;

    [Test]
    public async Task RepoPolicies_EndToEnd()
    {
        TestContext.WriteLine("Repository merge policy handling");
        TestContext.WriteLine("Running tests...");

        var repoUrl = GetGitHubRepoUrl(_repoName);

        // The RepoPolicies logic does a partial string match for the branch name in the base,
        // so it's important that this branch name not be a substring or superstring of another branch name
        var branchName = GetTestBranchName();

        TestContext.WriteLine("Enabling pull request merging");
        await SetRepositoryPolicies(repoUrl, branchName, ["--merge-prs", "true"]);
        var mergeEnabledPolicies = await GetRepositoryPolicies(repoUrl, branchName);
        var expectedMergeEnabledPolicies = $"{repoUrl} @ {branchName}\r\n  Merge PRs: True\r\n  Ignored Checks:\r\n";
        mergeEnabledPolicies.Should().BeEquivalentTo(
            expectedMergeEnabledPolicies,
            "Repository pull request merging was not enabled");

        TestContext.WriteLine("Adding ignored checks");
        await SetRepositoryPolicies(repoUrl, branchName, ["--merge-prs", "true", "--ignore-checks", "A,B"]);
        var policiesWithIgnoredChecks = await GetRepositoryPolicies(repoUrl, branchName);
        var expectedPoliciesWithIgnoredChecks =
            $"{repoUrl} @ {branchName}\r\n  Merge PRs: True\r\n  Ignored Checks:\r\n    - A\r\n    - B\r\n";
        policiesWithIgnoredChecks.Should().BeEquivalentTo(
            expectedPoliciesWithIgnoredChecks,
            "Repository ignored checks were not updated");

        TestContext.WriteLine("Disabling pull request merging and clearing ignored checks");
        await SetRepositoryPolicies(repoUrl, branchName, ["--merge-prs", "false"]);
        var resetPolicies = await GetRepositoryPolicies(repoUrl, branchName);
        var expectedResetPolicies = $"{repoUrl} @ {branchName}\r\n  Merge PRs: False\r\n  Ignored Checks:\r\n";
        resetPolicies.Should().BeEquivalentTo(
            expectedResetPolicies,
            "Repository merge settings were not reset");
    }
}
