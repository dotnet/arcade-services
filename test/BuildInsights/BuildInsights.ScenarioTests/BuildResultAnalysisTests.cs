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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Octokit;
using static BuildInsights.ScenarioTests.TestParameters;
using Repository = Octokit.Repository;

namespace BuildInsights.ScenarioTests;

[TestFixture]
public class BuildResultAnalysisTests
{
    // Makes a PR that includes breaking tests
    [Test]
    public async Task ValidatePRWithBreakingTests()
    {
        string originalOwner = "maestro-auth-test";
        string botOwnerName = "dotnet-helix-low-bot";
        string repoName = "build-result-analysis-test";
        string testBranchName = "scenarioTestsOnHelixStaging";
        string pullRequestHeadName = $"{botOwnerName}:{testBranchName}";
        string pullRequestTitle = "Scenario Test Build Analysis PR";
        TestGitHubInformation? testGitHubInformation = null;

        try
        {
            DateTimeOffset _start = DateTimeOffset.UtcNow;
            TestContext.WriteLine("Get target branch");
            Repository originalRepo = await GitHubApi.Repository.Get(originalOwner, repoName);
            long originalRepoId = originalRepo.Id;

            await GitHubTestHelper.CleanUpForks(repoName, botOwnerName);
            testGitHubInformation = await GitHubTestHelper.CreateScenarioTestEnvironment(
                originalOwner,
                botOwnerName,
                testBranchName,
                pullRequestTitle,
                pullRequestHeadName,
                repoName,
                originalRepoId);

            TestContext.WriteLine("Initialize storage object");
            IContextualStorage storage = testData.BlobContextualStorage;
            storage.SetContext($"{originalOwner}/{repoName}/{testGitHubInformation.Commit.Sha}");
            TestContext.WriteLine($"Initialized storage object for commit.Sha: {testGitHubInformation.Commit.Sha}");

            while (true)
            {
                CheckRunsResponse checks = await GitHubApi.Check.Run.GetAllForReference(
                    originalOwner,
                    repoName,
                    testGitHubInformation.Commit.Sha);

                if (checks.TotalCount > 0 && checks.CheckRuns.All(x => x.Status == CheckStatus.Completed))
                {
                    var buildResultAnalysisCheck = checks.CheckRuns.First(c =>
                        c.App.Id == GitHubTestHelper.StagingBuildAnalysisAppId &&
                        c.Name == "Build Analysis");

                    TestContext.WriteLine($"Found check run {buildResultAnalysisCheck.Id}");

                    Regex r = new("<!-- SnapshotId: (.*?) -->");
                    Match m = r.Match(buildResultAnalysisCheck.Output.Text);
                    string snapshotId = m.Groups[1].Value;
                    TestContext.WriteLine($"SnapshotId: {snapshotId}");

                    Stream? stream = await storage.TryGetAsync(
                        $"analysis-blob-{snapshotId}.json",
                        CancellationToken.None);
                    stream.Should().NotBeNull("because the snapshot '{0}' was retrieved.", snapshotId);

                    var options = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
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

            var gitHubChecksService = TestParameters.ServiceProvider.GetRequiredService<IGitHubChecksService>();
            IEnumerable<GitHub.Models.CheckRun> checkRunsWithBuildId =
                await gitHubChecksService.GetBuildCheckRunsAsync(
                    $"{originalOwner}/{repoName}",
                    testGitHubInformation.Commit.Sha);
            DateTimeOffset end = SystemClock.UtcNow;

            List<(TestRunSignatureWithName signature, TestCounts count, int partitionId)> aggregatedCounts = [];
            foreach (string organization in checkRunsWithBuildId.Select(t => t.Organization!).Distinct())
            {
                int[] buildIds = checkRunsWithBuildId
                    .Where(t => t.Organization!.Equals(organization, StringComparison.OrdinalIgnoreCase))
                    .Select(c => c.AzureDevOpsBuildId)
                    .ToArray();
                buildIds.Should().NotBeEmpty("associated azure devops builds should be present");

                aggregatedCounts.AddRange(
                    await testData.TestAggregatorService.GetAggregatedCounts(
                        organization,
                        "public",
                        _start,
                        end,
                        CancellationToken.None,
                        buildIds));
            }

            TestContext.WriteLine($"Found {aggregatedCounts.Count} tests with counts");
            foreach (var test in aggregatedCounts)
            {
                TestContext.WriteLine($"Test {test.signature}, with counts F:{test.count.Failed}/P:{test.count.Passed}/R:{test.count.PassedOnRerun}");
            }

            List<(TestRunSignatureWithName signature, TestCounts count, int partitionId)> passingTest = [.. aggregatedCounts.Where(a => a.count.Passed > 0)];
            passingTest.Should().HaveCount(1);

            (TestRunSignatureWithName signature, TestCounts count, int partitionId) = passingTest.First();
            count.Passed.Should().Be(1);
            partitionId.Should().Be(4);
            signature.Run.BuildDefinitionName.Should().Be("build-result-analysis-test");
            signature.Run.Repository.Should().Be("maestro-auth-test/build-result-analysis-test");
            signature.ArgumentHash.Should().BeEmpty();
            signature.ArgumentHash.Should().BeEmpty();
            signature.TestName.Should()
                .Be("TestAggregatorHelixTests.TestAggregatorHelixTests.TestResultDeterminedByCorrelationPayload");

            string escapeMechanismComment = $"{BuildAnalysisEscapeMechanismHelper.ChangeToGreenCommand} Build Analysis PostDeployment Test";
            await GitHubApi.Issue.Comment.Create(originalOwner, repoName, testGitHubInformation.PullRequestId, escapeMechanismComment);

            DateTimeOffset limitTimeToWaitForUpdate = SystemClock.UtcNow.AddMinutes(15);
            while (true)
            {
                if (SystemClock.UtcNow > limitTimeToWaitForUpdate)
                {
                    Assert.Fail(
                        $"The check run was not updated to succeeded after sending {BuildAnalysisEscapeMechanismHelper.ChangeToGreenCommand} " +
                        $"command for pull request  {originalOwner}/{repoName}/{testGitHubInformation.PullRequestId}.");
                }

                CheckRunsResponse checks = await GitHubApi.Check.Run.GetAllForReference(originalOwner, repoName, testGitHubInformation.Commit.Sha);
                if (checks.TotalCount > 0 && checks.CheckRuns.All(x => x.Status == CheckStatus.Completed))
                {
                    var buildResultAnalysisCheck = checks.CheckRuns.FirstOrDefault(c =>
                        c.App.Id == GitHubTestHelper.StagingBuildAnalysisAppId &&
                        c.Name == "Build Analysis");

                    if (buildResultAnalysisCheck?.Conclusion == CheckConclusion.Success)
                    {
                        break;
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(60));
            }
        }
        finally
        {
            if (testGitHubInformation != null)
            {
                await GitHubTestHelper.CleanUpEnvironment(
                    originalOwner,
                    botOwnerName,
                    repoName,
                    testGitHubInformation.PullRequestId,
                    testGitHubInformation.ForkedRepoId);
            }
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
