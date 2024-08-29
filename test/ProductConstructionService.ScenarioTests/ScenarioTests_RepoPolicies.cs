// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using NUnit.Framework;
using NUnit.Framework.Internal;

namespace ProductConstructionService.ScenarioTests;

[TestFixture]
[Category("PostDeployment")]
[Parallelizable]
internal class ScenarioTests_RepoPolicies : MaestroScenarioTestBase
{
    // The RepoPolicies logic does a partial string match for the branch name in the base,
    // so it's important that this branch name not be a substring or superstring of another branch name
    private readonly string _branchName = $"MaestroRepoPoliciesTestBranch_{Environment.MachineName}";
    private readonly string _repoName = TestRepository.TestRepo1Name;
    private TestParameters _parameters;

    [TearDown]
    public Task DisposeAsync()
    {
        _parameters.Dispose();
        return Task.CompletedTask;
    }

    [Test]
    public async Task ArcadeRepoPolicies_EndToEnd()
    {
        TestContext.WriteLine("Repository merge policy handling");
        TestContext.WriteLine("Running tests...");

        _parameters = await TestParameters.GetAsync();
        SetTestParameters(_parameters);

        var repoUrl = GetGitHubRepoUrl(_repoName);

        TestContext.WriteLine("Setting repository merge policy to empty");
        await SetRepositoryPolicies(repoUrl, _branchName);
        var emptyPolicies = await GetRepositoryPolicies(repoUrl, _branchName);
        var expectedEmpty = $"{repoUrl} @ {_branchName}\r\n- Merge Policies: []\r\n";
        emptyPolicies.Should().BeEquivalentTo(expectedEmpty, "Repository merge policy is not empty");

        TestContext.WriteLine("Setting repository merge policy to standard");
        await SetRepositoryPolicies(repoUrl, _branchName, ["--standard-automerge"]);
        var standardPolicies = await GetRepositoryPolicies(repoUrl, _branchName);
        var expectedStandard = $"{repoUrl} @ {_branchName}\r\n- Merge Policies:\r\n  Standard\r\n";
        standardPolicies.Should().BeEquivalentTo(expectedStandard, "Repository policy not set to standard");

        TestContext.WriteLine("Setting repository merge policy to all checks successful");
        await SetRepositoryPolicies(repoUrl, _branchName, ["--all-checks-passed", "--ignore-checks", "A,B"]);
        var allChecksPolicies = await GetRepositoryPolicies(repoUrl, _branchName);
        var expectedAllChecksPolicies = $"{repoUrl} @ {_branchName}\r\n- Merge Policies:\r\n  AllChecksSuccessful\r\n    ignoreChecks = \r\n" +
            "                   [\r\n" +
            "                     \"A\",\r\n" +
            "                     \"B\"\r\n" +
            "                   ]\r\n";
        allChecksPolicies.Should().BeEquivalentTo(expectedAllChecksPolicies, "Repository policy is incorrect for all checks successful case");
    }
}
