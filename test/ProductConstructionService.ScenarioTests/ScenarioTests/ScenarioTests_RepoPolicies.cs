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

        TestContext.WriteLine("Setting repository merge policy to standard");
        await SetRepositoryPolicies(repoUrl, branchName, ["--standard-automerge"]);
        var standardPolicies = await GetRepositoryPolicies(repoUrl, branchName);
        var expectedStandard = $"{repoUrl} @ {branchName}\r\n- Merge Policies:\r\n  Standard\r\n";
        standardPolicies.Should().BeEquivalentTo(expectedStandard, "Repository policy not set to standard");

        TestContext.WriteLine("Setting repository merge policy to all checks successful");
        await SetRepositoryPolicies(repoUrl, branchName, ["--all-checks-passed", "--ignore-checks", "A,B"]);
        var allChecksPolicies = await GetRepositoryPolicies(repoUrl, branchName);
        var expectedAllChecksPolicies = $"{repoUrl} @ {branchName}\r\n- Merge Policies:\r\n  AllChecksSuccessful\r\n    ignoreChecks = \r\n" +
            "                   [\r\n" +
            "                     \"A\",\r\n" +
            "                     \"B\"\r\n" +
            "                   ]\r\n";
        allChecksPolicies.Should().BeEquivalentTo(expectedAllChecksPolicies, "Repository policy is incorrect for all checks successful case");

        TestContext.WriteLine("Deleting the repository merge policies");
        await SetRepositoryPolicies(repoUrl, branchName);
        var emptyPolicies = await GetRepositoryPolicies(repoUrl, branchName);
        emptyPolicies.Should().BeEquivalentTo(string.Empty, "Repository merge policy is not empty");
    }
}
