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
public class BuildInsightsEndToEndTests
{
    [Test]
    public async Task ValidatePRWithBreakingTests()
    {
        string testBranchName = $"scenario-tests/{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";

        await using TestGitHubInformation testGitHubInformation = await GitHubTestHelper.CreateTestPr(
            GitHubTestOrg,
            GitHubTestRepo,
            testBranchName,
            $"[Scenario Tests] {nameof(BuildInsightsEndToEndTests)}.{nameof(ValidatePRWithBreakingTests)}",
            $"{GitHubTestOrg}:{testBranchName}");

        DateTimeOffset _start = DateTimeOffset.UtcNow;

        var storage = ScenarioTestConfiguration.ServiceProvider.GetRequiredService<IContextualStorage>();
        storage.SetContext($"{GitHubTestOrg}/{GitHubTestRepo}/{testGitHubInformation.Commit.Sha}");

        await WaitForCompletedBuilds(testGitHubInformation);
        await VerifyFailedTestsAndChecks(testGitHubInformation, _start);
        await PostBaGreenCommentAndWaitForCheckSuccess(testGitHubInformation);
    }

    private static async Task WaitForCompletedBuilds(TestGitHubInformation testGitHubInformation)
    {
        var options = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

        while (true)
        {
            // TODO: Throw after some limit

            CheckRunsResponse checks = await GitHubApi.Check.Run.GetAllForReference(
                testGitHubInformation.Owner,
                testGitHubInformation.RepoName,
                testGitHubInformation.Commit.Sha);

            if (checks.TotalCount > 0 && checks.CheckRuns.All(x => x.Status == CheckStatus.Completed))
            {
                var buildResultAnalysisCheck = checks.CheckRuns
                    .First(c => c.App.Id == GitHubAppSettings.AppId && c.Name == GitHubAppSettings.AppName);

                TestContext.WriteLine($"Found check run {buildResultAnalysisCheck.Id}");

                Regex r = new("<!-- SnapshotId: (.*?) -->");
                Match m = r.Match(buildResultAnalysisCheck.Output.Text);
                string snapshotId = m.Groups[1].Value;
                TestContext.WriteLine($"SnapshotId: {snapshotId}");

                Stream? stream = await ScenarioTestConfiguration.ServiceProvider.GetRequiredService<IContextualStorage>().TryGetAsync(
                    $"analysis-blob-{snapshotId}.json",
                    CancellationToken.None);
                stream.Should().NotBeNull("because the snapshot '{0}' was retrieved.", snapshotId);

                var data = await JsonSerializer.DeserializeAsync<MergedBuildResultAnalysis>(
                    stream,
                    options,
                    CancellationToken.None);
                data.Should().NotBeNull();

                // This is because there's a race condition when the checks are of completed status, but we're still waiting for the second
                //   pipeline's results to be analyzed and consolidated with the previously completed pipeline results on the check.
                if (data.CompletedPipelines.Count < 2)
                {
                    TestContext.WriteLine("Waiting for the other build!");
                    await Task.Delay(TimeSpan.FromSeconds(60));
                    continue;
                }

                foreach (var pipeline in data.CompletedPipelines)
                {
                    TestContext.WriteLine($"Found associated, completed pipeline: {pipeline.PipelineName}, #{pipeline.BuildId}");
                }

                buildResultAnalysisCheck.Conclusion?.Value.Should().Be(CheckConclusion.Failure);
                ValidateSnapshotData(data);
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(60));
        }
    }

    private static async Task VerifyFailedTestsAndChecks(TestGitHubInformation testGitHubInformation, DateTimeOffset _start)
    {
        var gitHubChecksService = ScenarioTestConfiguration.ServiceProvider.GetRequiredService<IGitHubChecksService>();
        IEnumerable<GitHub.Models.CheckRun> checkRunsWithBuildId =
            await gitHubChecksService.GetBuildCheckRunsAsync(
                $"{GitHubTestOrg}/{testGitHubInformation.RepoName}",
                testGitHubInformation.Commit.Sha);
        DateTimeOffset end = SystemClock.UtcNow;

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
                    cancellationToken: CancellationToken.None);

                foreach (TestRun run in testRuns)
                {
                    List<TestCaseResult> results = await testClient.GetTestResultsAsync(
                        "public",
                        run.Id,
                        cancellationToken: CancellationToken.None);

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
        passingResult.AutomatedTestName.Should()
            .Be("TestAggregatorHelixTests.TestAggregatorHelixTests.TestResultDeterminedByCorrelationPayload");
    }

    private static async Task PostBaGreenCommentAndWaitForCheckSuccess(TestGitHubInformation testGitHubInformation)
    {
        string escapeMechanismComment = $"{BuildAnalysisEscapeMechanismHelper.ChangeToGreenCommand} Build Analysis PostDeployment Test";
        await GitHubApi.Issue.Comment.Create(GitHubTestOrg, testGitHubInformation.RepoName, testGitHubInformation.PullRequestId, escapeMechanismComment);

        DateTimeOffset limitTimeToWaitForUpdate = SystemClock.UtcNow.AddMinutes(15);
        while (true)
        {
            if (SystemClock.UtcNow > limitTimeToWaitForUpdate)
            {
                Assert.Fail(
                    $"The check run was not updated to succeeded after sending {BuildAnalysisEscapeMechanismHelper.ChangeToGreenCommand} " +
                    $"command for pull request  {GitHubTestOrg}/{testGitHubInformation.RepoName}/{testGitHubInformation.PullRequestId}.");
            }

            CheckRunsResponse checks = await GitHubApi.Check.Run.GetAllForReference(GitHubTestOrg, testGitHubInformation.RepoName, testGitHubInformation.Commit.Sha);
            if (checks.TotalCount > 0 && checks.CheckRuns.All(x => x.Status == CheckStatus.Completed))
            {
                var buildResultAnalysisCheck = checks.CheckRuns
                    .First(c => c.App.Id == GitHubAppSettings.AppId && c.Name == GitHubAppSettings.AppName);

                if (buildResultAnalysisCheck?.Conclusion == CheckConclusion.Success)
                {
                    break;
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(60));
        }
    }

    private static void ValidateSnapshotData(MergedBuildResultAnalysis data)
    {
        data.CompletedPipelines.Should().HaveCount(2);

        List<BuildResultAnalysis> pipeline1 = [.. data.CompletedPipelines.Where(p => p.PipelineName == "build-result-analysis-test")];
        pipeline1.Should().HaveCount(1);
        pipeline1[0].HasBuildFailures.Should().BeTrue();
        pipeline1[0].HasTestFailures.Should().BeTrue();
        pipeline1[0].TestResults.Where(r => r.TestCaseResult.Name == "BuildResultAnalysisTest.Tests.TestAlwaysFailing")
            .Should().HaveCount(1);

        List<BuildResultAnalysis> pipeline2 = [.. data.CompletedPipelines.Where(p => p.PipelineName == "maestro-auth-test.build-result-analysis-test")];
        pipeline2.Count.Should().Be(1);
        pipeline2[0].HasBuildFailures.Should().BeTrue();
        pipeline2[0].HasTestFailures.Should().BeTrue();
        pipeline2[0].TestResults.Where(r => r.TestCaseResult.Name == "SecondaryTests.Tests.FailingTest")
            .Should().HaveCount(1);
    }
}
