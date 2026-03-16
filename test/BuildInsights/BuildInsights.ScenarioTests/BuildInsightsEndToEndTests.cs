// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using BuildInsights.AzureStorage.Cache;
using BuildInsights.BuildAnalysis;
using BuildInsights.BuildAnalysis.Models;
using BuildInsights.GitHub;
using BuildInsights.Utilities.AzureDevOps;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.TeamFoundation.TestManagement.WebApi;
using Microsoft.VisualStudio.Services.WebApi;
using NUnit.Framework;
using Octokit;
using static BuildInsights.ScenarioTests.ScenarioTestConfiguration;
using TestCaseResult = Microsoft.TeamFoundation.TestManagement.WebApi.TestCaseResult;

namespace BuildInsights.ScenarioTests;

[TestFixture]
[Category("PostDeployment")]
public class BuildInsightsEndToEndTests
{
    private const string TestName = "TestAggregatorHelixTests.TestAggregatorHelixTests.TestResultDeterminedByCorrelationPayload";
    private const string KnownIssueTitle = "Test Known Issue";
    private readonly string _testPrTitle = $"[Scenario Tests] {nameof(BuildInsightsEndToEndTests)}.{nameof(ValidatePRWithBreakingTests)}";

    [Test]
    public async Task ValidatePRWithBreakingTests()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(45));
        CancellationToken cancellationToken = cts.Token;

        string testBranchName = $"scenario-tests/{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";

        await using TestGitHubInformation testGitHubInformation = await GitHubTestHelper.CreateTestPr(
            GitHubTestOrg,
            GitHubTestRepo,
            testBranchName,
            _testPrTitle,
            $"{GitHubTestOrg}:{testBranchName}");

        DateTimeOffset _start = DateTimeOffset.UtcNow;

        var storage = ScenarioTestConfiguration.ServiceProvider.GetRequiredService<IContextualStorage>();
        storage.SetContext($"{testGitHubInformation.Repository}/{testGitHubInformation.Commit.Sha}");

        await WaitForCompletedBuilds(testGitHubInformation, cancellationToken);
        await VerifyFailedTestsAndChecks(testGitHubInformation, _start, cancellationToken);
        await PostBaGreenCommentAndWaitForCheckSuccess(testGitHubInformation, cancellationToken);
    }

    private static async Task WaitForCompletedBuilds(
        TestGitHubInformation testGitHubInformation,
        CancellationToken cancellationToken)
    {
        var options = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        var storageCache = ScenarioTestConfiguration.ServiceProvider.GetRequiredService<IContextualStorage>();
        Regex r = new("<!-- SnapshotId: (.*?) -->");

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CheckRunsResponse checks = await GitHubApi.Check.Run.GetAllForReference(
                testGitHubInformation.Owner,
                testGitHubInformation.RepoName,
                testGitHubInformation.Commit.Sha);

            if (checks.TotalCount <= 0 || !checks.CheckRuns.All(x => x.Status == CheckStatus.Completed))
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                continue;
            }

            CheckRun? buildInsightsCheck = checks.CheckRuns.FirstOrDefault(c => c.App.Id == GitHubAppSettings.AppId
                                                                             && c.Name == GitHubAppSettings.AppName);

            if (buildInsightsCheck is null)
            {
                TestContext.WriteLine("Build insights check not found, waiting...");
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                continue;
            }

            TestContext.WriteLine($"Found check run {buildInsightsCheck.Id}");

            if (EnvironmentName != "Development")
            {
                Match m = r.Match(buildInsightsCheck.Output.Text);
                string snapshotId = m.Groups[1].Value;
                TestContext.WriteLine($"SnapshotId: {snapshotId}");

                Stream? stream = await storageCache.TryGetAsync($"analysis-blob-{snapshotId}.json", cancellationToken);
                stream.Should().NotBeNull("because the snapshot '{0}' was retrieved.", snapshotId);

                var data = await JsonSerializer.DeserializeAsync<MergedBuildResultAnalysis>(stream, options, cancellationToken);
                data.Should().NotBeNull();

                // This is because there's a race condition when the checks are of completed status, but we're still waiting for the second
                //   pipeline's results to be analyzed and consolidated with the previously completed pipeline results on the check.
                if (data.CompletedPipelines.Count < 2)
                {
                    TestContext.WriteLine("Waiting for the other build!");
                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                    continue;
                }

                foreach (var pipeline in data.CompletedPipelines)
                {
                    TestContext.WriteLine($"Found associated, completed pipeline: {pipeline.PipelineName}, #{pipeline.BuildId}");
                }

                ValidateSnapshotData(data);
            }

            buildInsightsCheck.Conclusion.Should().Be(CheckConclusion.Failure);
            buildInsightsCheck.Conclusion.Should().Be(GitHub.Models.CheckConclusion.Failure, "there are test failures");
            buildInsightsCheck.Output.Text.Should().Contain("Test Failures (2 tests failed)")
                .And.Contain("build-insights-test-1")
                .And.Contain("Known test errors")
                .And.Contain(KnownIssueTitle);
            break;
        }
    }

    private static async Task VerifyFailedTestsAndChecks(
        TestGitHubInformation testGitHubInformation,
        DateTimeOffset _start,
        CancellationToken cancellationToken)
    {
        var gitHubChecksService = ScenarioTestConfiguration.ServiceProvider.GetRequiredService<IGitHubChecksService>();
        IEnumerable<GitHub.Models.CheckRun> checkRunsWithBuildId = await gitHubChecksService.GetBuildCheckRunsAsync(
            testGitHubInformation.Repository,
            testGitHubInformation.Commit.Sha);

        DateTimeOffset end = DateTimeOffset.UtcNow;

        var vssConnectionProvider = ScenarioTestConfiguration.ServiceProvider.GetRequiredService<VssConnectionProvider>();
        List<(TestCaseResult result, string organization, string project, TestRun run)> allTestResults = [];
        foreach (string organization in checkRunsWithBuildId.Select(t => t.Organization!).Distinct())
        {
            int[] buildIds = checkRunsWithBuildId
                .Where(t => t.Organization!.Equals(organization, StringComparison.OrdinalIgnoreCase))
                .Select(c => c.AzureDevOpsBuildId)
                .ToArray();
            buildIds.Should().NotBeEmpty("associated azure devops builds should be present");

            using VssConnection connection = vssConnectionProvider.GetConnection(organization);
            TestManagementHttpClient testClient = connection.GetClient<TestManagementHttpClient>();

            foreach (int buildId in buildIds)
            {
                List<TestRun> testRuns = await testClient.QueryTestRunsAsync(
                    "public",
                    _start.DateTime,
                    end.DateTime,
                    buildIds: [buildId],
                    cancellationToken: cancellationToken);

                foreach (TestRun run in testRuns)
                {
                    List<TestCaseResult> results = await testClient.GetTestResultsAsync(
                        "public",
                        run.Id,
                        cancellationToken: cancellationToken);

                    foreach (TestCaseResult result in results)
                    {
                        allTestResults.Add((result, organization, "public", run));
                    }
                }
            }
        }

        TestContext.WriteLine($"Found {allTestResults.Count} test results");
        foreach (var (result, org, project, run) in allTestResults)
        {
            TestContext.WriteLine($"Test {result.AutomatedTestName}, outcome: {result.Outcome}, run: {run.Name}");
        }

        var passingResults = allTestResults.Where(t => t.result.Outcome == "Passed").ToList();
        passingResults.Should().HaveCount(1);

        var (passingResult, _, _, passingRun) = passingResults.First();
        passingResult.AutomatedTestName.Should().Be(TestName);
    }

    private static async Task PostBaGreenCommentAndWaitForCheckSuccess(
        TestGitHubInformation testGitHubInformation,
        CancellationToken cancellationToken)
    {
        string escapeMechanismComment = $"{BuildAnalysisEscapeMechanismHelper.ChangeToGreenCommand} {GitHubAppSettings.AppName} PostDeployment Test";
        await GitHubApi.Issue.Comment.Create(
            testGitHubInformation.Owner,
            testGitHubInformation.RepoName,
            testGitHubInformation.PullRequestId,
            escapeMechanismComment);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CheckRunsResponse checks = await GitHubApi.Check.Run.GetAllForReference(GitHubTestOrg, testGitHubInformation.RepoName, testGitHubInformation.Commit.Sha);
            if (checks.TotalCount > 0 && checks.CheckRuns.All(x => x.Status == CheckStatus.Completed))
            {
                var buildResultAnalysisCheck = checks.CheckRuns
                    .FirstOrDefault(c => c.App.Id == GitHubAppSettings.AppId
                                      && c.Name == GitHubAppSettings.AppName);

                if (buildResultAnalysisCheck?.Conclusion == CheckConclusion.Success)
                {
                    break;
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
        }
    }

    private static void ValidateSnapshotData(MergedBuildResultAnalysis data)
    {
        data.CompletedPipelines.Should().HaveCount(2);

        BuildResultAnalysis? pipeline = data.CompletedPipelines.FirstOrDefault(p => p.PipelineName == "build-insights-test-1");
        pipeline.Should().NotBeNull();
        pipeline.HasBuildFailures.Should().BeTrue();
        pipeline.HasTestFailures.Should().BeTrue();
        pipeline.TestResults.Should().Contain(r => r.TestCaseResult.Name == TestName);

        pipeline = data.CompletedPipelines.FirstOrDefault(p => p.PipelineName == "build-insights-test-2");
        pipeline.Should().NotBeNull();
        pipeline.HasBuildFailures.Should().BeFalse();
        pipeline.HasTestFailures.Should().BeFalse();
    }
}
