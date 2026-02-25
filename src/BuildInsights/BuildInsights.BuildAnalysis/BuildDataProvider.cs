// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using BuildInsights.Utilities.AzureDevOps;
using Maestro.Common;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.TeamFoundation.TestManagement.WebApi;
using Microsoft.VisualStudio.Services.WebApi;

#nullable disable
namespace BuildInsights.BuildAnalysis;

public interface IBuildDataService
{
    Task<Models.Build> GetBuildAsync(string orgId, string projectId, int buildId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Models.Build>> GetFailedBuildsAsync(string orgId, string projectId, string repository, CancellationToken cancellationToken);
    Task<List<Models.TestRunDetails>> GetFailingTestsForBuildAsync(
        Models.Build build,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<Models.TestRunDetails>> GetAllFailingTestsForBuildAsync(
        Models.Build build,
        CancellationToken cancellationToken);
    Task<List<Models.TestHistoryByBranch>> GetTestHistoryAsync(
        string orgId,
        string projectId,
        string testName,
        DateTimeOffset maxCompleted,
        CancellationToken cancellationToken);
    Task<Models.TestCaseResult> GetTestResultByIdAsync(string orgId, string projectId, int testRunId, int testCaseResultId, CancellationToken cancellationToken, Models.ResultDetails resultDetails = Models.ResultDetails.None);
    Task<IReadOnlyList<Models.Build>> GetLatestBuildsForBranchAsync(string orgId, string projectId, int definitionId, Models.GitRef targetBranch, DateTimeOffset latestDate, CancellationToken cancellationToken);
    Task<IReadOnlyList<Models.TimelineRecord>> GetLatestBuildTimelineRecordsAsync(string orgId, string projectId, int buildId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Models.TimelineRecord>> GetBuildTimelineRecordsAsync(string orgId, string projectId, int buildId, Guid timelineId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Models.TimelineRecord>> GetTimelineRecordsFromAllAttempts(IReadOnlyCollection<Models.TimelineRecord> latestTimelineRecords, string orgId, string projectId, int buildId, CancellationToken cancellationToken);
    Task<Models.BuildConfiguration> GetBuildConfiguration(string orgId, string projectId, int buildId, string artifactName, string fileName, CancellationToken cancellationToken);
    Task<string> GetProjectName(string orgId, string projectId);

    Task<ImmutableList<Models.TestRunDetails>> GetTestsForBuildAsync(
        Models.Build build,
        CancellationToken cancellationToken);

    IAsyncEnumerable<Models.HelixMetadata> GetTestRunMetaDataAsync(
        ImmutableList<string> attachmentUrl,
        CancellationToken cancellationToken);
    Task<Stream> GetLogContent(string orgId, string project, int buildId, int logId);
}

public sealed class BuildDataProvider : IBuildDataService
{
    private readonly VssConnectionProvider _connections;
    private readonly IHttpClientFactory _httpFactory;

    private readonly ISystemClock _systemClock;

    private readonly ILogger<BuildDataProvider> _logger;

    public BuildDataProvider(
        VssConnectionProvider connections,
        ISystemClock systemClock,
        IHttpClientFactory httpFactory,
        ILogger<BuildDataProvider> logger)
    {
        _connections = connections;
        _httpFactory = httpFactory;
        _systemClock = systemClock;
        _logger = logger;
    }

    public async Task<Models.Build> GetBuildAsync(string orgId, string projectId, int buildId, CancellationToken cancellationToken)
    {
        _logger.LogInformation($"Fetching build information from Azure DevOps for build '{buildId}' in project '{projectId}' and org '{orgId}'.");

        using var connection = _connections.GetConnection(orgId);
        BuildHttpClient buildClient = connection.GetClient<BuildHttpClient>();

        Build azdoBuild;
        try
        {
            azdoBuild = await buildClient.GetBuildAsync(
                projectId,
                buildId,
                cancellationToken: cancellationToken
            );
        }
        catch (Microsoft.TeamFoundation.Build.WebApi.BuildNotFoundException e)
        {
            throw new BuildNotFoundException($"Unable to bind build '{buildId}' in project '{projectId}'", e);
        }

        return MapModel(azdoBuild);
    }

    private static readonly Regex s_helixMetadataFilePattern = new(@"^__helix_metadata_.*\.json\.gz$");

    private static readonly VssJsonMediaTypeFormatter s_helixMetadataFormatter = new();

    public async Task<ImmutableList<Models.TestRunDetails>> GetTestsForBuildAsync(
        Models.Build build,
        CancellationToken cancellationToken)
    {
        using var connection = _connections.GetConnection(build.OrganizationName);
        TestManagementHttpClient testClient = connection.GetClient<TestManagementHttpClient>();
        var builder = ImmutableList.CreateBuilder<Models.TestRunDetails>();
        IEnumerable<TestRun> runs = await GetCurrentTestRunsForBuild(build, cancellationToken);
        foreach (var run in runs)
        {
            List<Models.TestCaseResult> failedResults = [];
            List<Models.TestCaseResult> passedOnRerunResults = [];
            if (run.RunStatistics.Any(s => s.Outcome == "Failed"))
            {
                var shallowResults = new List<TestCaseResult>();
                // There are failed tests, we need to grab the details
                while (true)
                {
                    List<TestCaseResult> partial = await testClient.GetTestResultsAsync(
                        build.ProjectName,
                        run.Id,
                        ResultDetails.SubResults,
                        outcomes: new[]
                        {
                            TestOutcome.Failed
                        },
                        skip: shallowResults.Count,
                        cancellationToken: cancellationToken
                    );
                    if (partial.Count == 0)
                        break;

                    shallowResults.AddRange(partial);
                }

                IEnumerable<TestCaseResult> filled = await FillSubResultsAsync(build.OrganizationName, shallowResults, cancellationToken);
                failedResults = filled.Select(result => MapModel(result)).ToList();
            }

            List<TestAttachment> attachments = await testClient.GetTestRunAttachmentsAsync(
                build.ProjectName,
                run.Id,
                cancellationToken: cancellationToken
            );
            HashSet<int> existingTests = null;
            foreach (TestAttachment attachment in attachments)
            {
                if (!s_helixMetadataFilePattern.IsMatch(attachment.FileName))
                {
                    // This is some other attachment that isn't one of our metadata ones, move on
                    continue;
                }

                // Use the client inside the test management to get authentication, which is necessary
                // to pull the attachment streams
                // The attachment is a gzipped, json serialized HelixMetadata structure
                await using Stream rawStream = await testClient.HttpClient.GetStreamAsync(attachment.Url);
                await using var stream = new GZipStream(rawStream, CompressionMode.Decompress);
                var metadata = (HelixMetadataContract)await s_helixMetadataFormatter.ReadFromStreamAsync(
                    typeof(HelixMetadataContract),
                    stream,
                    null,
                    null
                );

                if (metadata.RerunTests != null)
                {
                    existingTests ??= failedResults.Select(f => f.Id).ToHashSet();
                    IEnumerable<Models.TestCaseResult> rerunCases = metadata.RerunTests
                        .Where(r => !existingTests.Contains(r.Id)) // If we already got the reruns from the failed, don't double add them
                        .Select(result => MapModel(result, build.Id, build.ProjectName, run.Id));
                    passedOnRerunResults.AddRange(rerunCases);
                }
            }

            builder.Add(
                new Models.TestRunDetails(
                    MapModel(run),
                    failedResults.Concat(passedOnRerunResults),
                    run.CompletedDate
                )
            );
        }

        return builder.ToImmutable();
    }

    public async IAsyncEnumerable<Models.HelixMetadata> GetTestRunMetaDataAsync(ImmutableList<string> attachmentUrl, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (string attachment in attachmentUrl)
        {
            // The attachment is a gzipped, json serialized HelixMetadata structure

            await using Stream rawStream = await _httpFactory.CreateClient(GetType().Name).GetStreamAsync(attachment);
            await using var stream = new GZipStream(rawStream, CompressionMode.Decompress);
            var helixMetaDataJson =
                (HelixMetadataContract)await s_helixMetadataFormatter.ReadFromStreamAsync(
                    typeof(HelixMetadataContract),
                    stream,
                    content: null,
                    formatterLogger: null,
                    cancellationToken: cancellationToken
                );
            yield return MapModel(helixMetaDataJson);
        }
    }

    public async Task<List<Models.TestRunDetails>> GetFailingTestsForBuildAsync(
        Models.Build build,
        CancellationToken cancellationToken)
    {
        // Get test runs associated with the give build ID

        IEnumerable<TestRun> currentRuns = await GetCurrentTestRunsForBuild(build, cancellationToken);
        IEnumerable<TestRun> testRunsWithFailedOutcome = currentRuns
            .Where(run => run.RunStatistics
                .Any(stat => Models.TestOutcomeValue.Failed == stat.Outcome));

        // Get the test results for runs with failures
        return await GetFailingTestsFromRunAsync(build.OrganizationName, testRunsWithFailedOutcome, build.ProjectName, cancellationToken);
    }

    private async Task<IEnumerable<TestRun>> GetCurrentTestRunsForBuild(Models.Build build, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Fetching test run information from Azure DevOps for build '{buildId}' in project '{projectName}' and org '{orgName}'.",
            build.Id,
            build.ProjectName,
            build.OrganizationName
        );

        List<TestRun> testRuns = await GetTestRuns(build, cancellationToken);

        // Get the latest run of stage/phase/job
        // Look at runStatistics to find runs with failed tests
        IEnumerable<TestRun> currentRuns = testRuns
            .GroupBy(
                run => new
                {
                    run.Name,
                    run.PipelineReference?.PhaseReference?.PhaseName,
                    run.PipelineReference?.StageReference?.StageName,
                    run.PipelineReference?.JobReference?.JobName
                }
            )
            .SelectMany(group => group.Where(testRun =>
                testRun.PipelineReference.JobReference.Attempt == group.Max(t => t.PipelineReference.JobReference.Attempt)));

        return currentRuns;
    }

    public async Task<IReadOnlyList<Models.TestRunDetails>> GetAllFailingTestsForBuildAsync(
        Models.Build build,
        CancellationToken cancellationToken)
    {
        // Get test runs associated with the give build ID
        IEnumerable<TestRun> testRuns = await GetTestRuns(build, cancellationToken);

        // Look at runStatistics to find runs with failed builds
        IEnumerable<TestRun> testRunsWithFailedOutcome = testRuns.Where(
            run => run.RunStatistics.Any(
                stat => Models.TestOutcomeValue.Failed == stat.Outcome
            )
        );

        return await GetFailingTestsFromRunAsync(build.OrganizationName, testRunsWithFailedOutcome, build.ProjectName, cancellationToken);
    }

    private async Task<List<TestRun>> GetTestRuns(Models.Build build, CancellationToken cancellationToken)
    {
        using var connection = _connections.GetConnection(build.OrganizationName);
        TestManagementHttpClient testClient = connection.GetClient<TestManagementHttpClient>();

        List<TestRun> testRuns = [];
        string continuationToken = null;
        do
        {
            var result = await testClient.QueryTestRunsAsync(
                build.ProjectName,
                (build.FinishTime?.AddDays(-3) ?? _systemClock.UtcNow.AddDays(-3)).DateTime,
                (build.FinishTime?.AddDays(3) ?? _systemClock.UtcNow).DateTime,
                buildIds: new[] { build.Id },
                continuationToken: continuationToken,
                cancellationToken: cancellationToken
            );

            testRuns.AddRange(result);

            continuationToken = result.ContinuationToken;
        } while (!string.IsNullOrEmpty(continuationToken));

        return testRuns;
    }

    public async Task<string> GetProjectName(string orgId, string projectId)
    {
        using var connection = _connections.GetConnection(orgId);
        ProjectHttpClient projectClient = connection.GetClient<ProjectHttpClient>();

        TeamProject teamProject = await projectClient.GetProject(id: projectId);
        return teamProject.Name;
    }

    private async Task<List<Models.TestRunDetails>> GetFailingTestsFromRunAsync(
        string orgId,
        IEnumerable<TestRun> testRunsWithFailedOutcome,
        string projectId,
        CancellationToken cancellationToken)
    {
        using var connection = _connections.GetConnection(orgId);
        TestManagementHttpClient testClient = connection.GetClient<TestManagementHttpClient>();
        var testCaseResultsByTestRun = new List<Models.TestRunDetails>();

        foreach (TestRun run in testRunsWithFailedOutcome)
        {
            List<TestCaseResult> allTestCaseResults = [];

            do
            {
                _logger.LogInformation($"Fetching test result information from Azure DevOps for run '{run.Id}' in project '{projectId}'  and org '{orgId}'.");
                IEnumerable<TestCaseResult> testCaseResults = await testClient.GetTestResultsAsync(
                    projectId,
                    run.Id,
                    outcomes: new List<TestOutcome> { TestOutcome.Failed },
                    detailsToInclude: ResultDetails.SubResults,
                    skip: allTestCaseResults.Count,
                    cancellationToken: cancellationToken
                );

                if (!testCaseResults.Any())
                {
                    break;
                }

                allTestCaseResults.AddRange(testCaseResults);
            } while (true);

            IEnumerable<TestCaseResult> filled = await FillSubResultsAsync(orgId, allTestCaseResults, cancellationToken);

            testCaseResultsByTestRun.Add(
                new Models.TestRunDetails(MapModel(run), filled.Select(result => MapModel(result)), run.CompletedDate)
            );
        }

        return testCaseResultsByTestRun;
    }

    private async Task<IEnumerable<TestCaseResult>> FillSubResultsAsync(
        string orgId,
        IEnumerable<TestCaseResult> results,
        CancellationToken cancellationToken)
    {
        using var connection = _connections.GetConnection(orgId);
        TestManagementHttpClient testClient = connection.GetClient<TestManagementHttpClient>();

        var dict = new ConcurrentDictionary<int, TestCaseResult>(results.ToDictionary(r => r.Id));
        var needSubTest = dict.Values
            .Where(v => v.ResultGroupType != ResultGroupType.None && (v.SubResults?.Count ?? 0) == 0)
            .ToList();

        await Parallel.ForEachAsync(
            needSubTest,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = 5,
                CancellationToken = cancellationToken
            },
            async (t, ct) =>
            {
                var item = await testClient.GetTestResultByIdAsync(
                    t.Project.Id,
                    int.Parse(t.TestRun.Id),
                    t.Id,
                    ResultDetails.SubResults,
                    cancellationToken: ct);
                dict[item.Id] = item;
            });

        return dict.Values;
    }

    public async Task<List<Models.TestHistoryByBranch>> GetTestHistoryAsync(
        string orgId,
        string projectId,
        string testName,
        DateTimeOffset maxCompleted,
        CancellationToken cancellationToken)
    {
        using var connection = _connections.GetConnection(orgId);
        TestManagementHttpClient testClient = connection.GetClient<TestManagementHttpClient>();
        var allHistory = new Dictionary<string, List<TestCaseResult>>();
        var displayNames = new Dictionary<string, string>();

        for (int i = 0; i < 4; i++)
        {
            var testHistoryQuery = new TestHistoryQuery
            {
                AutomatedTestName = testName,
                TrendDays = 7, // Maximum supported value is 7
                GroupBy = TestResultGroupBy.Branch,
                MaxCompleteDate = maxCompleted.AddDays(7 * i * -1).DateTime
            };

            do
            {
                _logger.LogInformation($"Fetching test history information from Azure DevOps for testName '{testName}' in project '{projectId}' and org '{orgId}'..");
                TestHistoryQuery testHistory = await testClient.QueryTestHistoryAsync(
                    testHistoryQuery,
                    projectId,
                    cancellationToken: cancellationToken
                );

                // The TestHistoryQuery groupBy groups may span multiple API calls
                // Merge duplicate groups so that a group appears only once in the end result
                foreach (TestResultHistoryForGroup testResultGroup in testHistory.ResultsForGroup)
                {
                    if (!allHistory.TryGetValue(testResultGroup.GroupByValue, out var list))
                    {
                        allHistory.Add(testResultGroup.GroupByValue, list = []);
                    }
                    list.AddRange(testResultGroup.Results.Where(testCaseResult => testCaseResult.CompletedDate <= maxCompleted));
                    displayNames.TryAdd(testResultGroup.GroupByValue, testResultGroup.DisplayName);
                }

                testHistoryQuery.ContinuationToken = testHistory.ContinuationToken;
            } while (!string.IsNullOrEmpty(testHistoryQuery.ContinuationToken));
        }

        return allHistory
            .Select(
                group => new Models.TestHistoryByBranch(Models.GitRef.Parse(displayNames[group.Key]), group.Value.Select(result => MapModel(result)))
            )
            .ToList();
    }

    public async Task<Models.TestCaseResult> GetTestResultByIdAsync(
        string orgId,
        string projectId,
        int testRunId,
        int testCaseId,
        CancellationToken cancellationToken,
        Models.ResultDetails resultDetails = Models.ResultDetails.None)
    {
        _logger.LogInformation($"Fetching test result information from Azure DevOps for test run '{testRunId}' and test case '{testCaseId}' in project '{projectId}'  and org '{orgId}'.");
        using var connection = _connections.GetConnection(orgId);
        TestManagementHttpClient testClient = connection.GetClient<TestManagementHttpClient>();
        TestCaseResult testResult =
            await testClient.GetTestResultByIdAsync(projectId, testRunId, testCaseId, detailsToInclude: MapModel(resultDetails), cancellationToken: cancellationToken);

        return MapModel(testResult);
    }

    public async Task<IReadOnlyList<Models.Build>> GetLatestBuildsForBranchAsync(
        string orgId,
        string projectId,
        int definitionId,
        Models.GitRef targetBranch,
        DateTimeOffset latestDate,
        CancellationToken cancellationToken)
    {
        using var connection = _connections.GetConnection(orgId);
        _logger.LogInformation($"Fetching latest builds from Azure DevOps for project '{projectId}'  and org '{orgId}' and branch '{targetBranch}'.");
        BuildHttpClient buildClient = connection.GetClient<BuildHttpClient>();
        IEnumerable<Build> latestCompletedBuildsForBranch = await buildClient.GetBuildsAsync(
            projectId,
            definitions: new[] { definitionId },
            repositoryType: "GitHub",
            branchName: targetBranch.Path,
            maxBuildsPerDefinition: 1,
            maxFinishTime: latestDate.UtcDateTime,
            statusFilter: BuildStatus.Completed,
            cancellationToken: cancellationToken
        );

        return latestCompletedBuildsForBranch.Select(MapModel).ToImmutableList();
    }

    public async Task<IReadOnlyList<Models.TimelineRecord>> GetLatestBuildTimelineRecordsAsync(
        string orgId,
        string projectId,
        int buildId,
        CancellationToken cancellationToken)
    {
        using var connection = _connections.GetConnection(orgId);
        BuildHttpClient buildClient = connection.GetClient<BuildHttpClient>();
        Microsoft.TeamFoundation.Build.WebApi.Timeline timeline = await buildClient.GetBuildTimelineAsync(
            projectId,
            buildId,
            cancellationToken: cancellationToken
        );

        return timeline?.Records.Select(MapModel).ToList() ?? [];
    }

    public async Task<IReadOnlyList<Models.TimelineRecord>> GetBuildTimelineRecordsAsync(
        string orgId,
        string projectId,
        int buildId,
        Guid timelineId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation($"Fetching build timeline information from Azure DevOps for build '{buildId}' in project '{projectId}' and org '{orgId}'.");
        using var connection = _connections.GetConnection(orgId);
        BuildHttpClient buildClient = connection.GetClient<BuildHttpClient>();
        Microsoft.TeamFoundation.Build.WebApi.Timeline timeline = await buildClient.GetBuildTimelineAsync(
            projectId,
            buildId,
            timelineId,
            cancellationToken: cancellationToken
        );

        return timeline?.Records.Select(MapModel).ToImmutableList() ?? [];
    }


    public async Task<IReadOnlyList<Models.TimelineRecord>> GetTimelineRecordsFromAllAttempts(
        IReadOnlyCollection<Models.TimelineRecord> latestTimelineRecords,
        string orgId,
        string projectId,
        int buildId,
        CancellationToken cancellationToken)
    {
        if (latestTimelineRecords == null || latestTimelineRecords.Count < 1) return new List<Models.TimelineRecord>();

        List<Guid> timelineIdsPreviousAttempts = latestTimelineRecords
            .SelectMany(r => r.PreviousAttempts)
            .Select(a => a.TimelineId)
            .Distinct()
            .ToList();

        List<Task<IReadOnlyList<Models.TimelineRecord>>> previousTimelineTask = timelineIdsPreviousAttempts
            .Select(timelineId => GetBuildTimelineRecordsAsync(orgId, projectId, buildId, timelineId, cancellationToken))
            .ToList();

        IReadOnlyList<Models.TimelineRecord>[] previousTimelineRecords = await Task.WhenAll(previousTimelineTask);
        IReadOnlyList<Models.TimelineRecord> recordsFromAllAttempts = latestTimelineRecords.Concat(previousTimelineRecords.SelectMany(r => r)).ToList();

        return recordsFromAllAttempts.DistinctBy(t => t.Id).ToList();
    }

    public async Task<Models.BuildConfiguration> GetBuildConfiguration(
        string orgId,
        string projectId,
        int buildId,
        string artifactName,
        string fileName,
        CancellationToken cancellationToken)
    {
        using var connection = _connections.GetConnection(orgId);
        BuildHttpClient buildClient = connection.GetClient<BuildHttpClient>();
        try
        {
            _logger.LogInformation($"Fetching BuildConfiguration from Azure DevOps for build '{buildId}' in project '{projectId}'  and org '{orgId}' from artifact {artifactName} and file name {fileName} ");
            BuildArtifact buildArtifact = await buildClient.GetArtifactAsync(projectId, buildId, artifactName, cancellationToken: cancellationToken);

            //Get artifact files information
            string fileJson = await GetFileAsync(orgId, projectId, buildId, artifactName, buildArtifact.Resource.Data, cancellationToken);
            Models.ArtifactDetailsFile fileInformation = JsonSerializer.Deserialize<Models.ArtifactDetailsFile>(fileJson);

            //Get fileId for build-configuration.json
            string fileId = fileInformation.Items.Where(f => f.Path.EndsWith("/" + fileName)).Select(f => f.Blob.Id).FirstOrDefault();

            //If the file is not found
            if (string.IsNullOrEmpty(fileId)) return null;

            string fileSettingJson = await GetFileAsync(orgId, projectId, buildId, artifactName, fileId, cancellationToken);

            return JsonSerializer.Deserialize<Models.BuildConfiguration>(fileSettingJson);
        }
        catch (ArtifactNotFoundException)
        {
            _logger.LogInformation($"Artifact: {artifactName} not found on build {buildId} in org {orgId}");
            return null;
        }
        catch (Exception e)
        {
            _logger.LogError(e, $"Not able to get file: {fileName} from artifact: {artifactName} on build: {buildId} in org {orgId}");
            return null;
        }
    }

    private async Task<string> GetFileAsync(
        string orgId,
        string projectId,
        int buildId,
        string artifactName,
        string fileId,
        CancellationToken cancellationToken)
    {
        using var connection = _connections.GetConnection(orgId);
        BuildHttpClient buildClient = connection.GetClient<BuildHttpClient>();
        Stream streamFile = await buildClient.GetFileAsync(projectId, buildId, artifactName, fileId, "", cancellationToken: cancellationToken);
        using var fileReader = new StreamReader(streamFile);
        return await fileReader.ReadToEndAsync();
    }

    public static Models.Branch GetTargetBranch(Build build)
    {
        if (build.Reason == BuildReason.PullRequest)
        {
            if (build.Parameters == null)
            {
                throw new ArgumentException($"Could not determine pull request target branch for build {build.Id} because build parameters is missing");
            }

            var props = JsonSerializer.Deserialize<JsonElement>(build.Parameters);
            foreach (JsonProperty e in props.EnumerateObject())
            {
                if (e.Name.Equals("system.pullrequest.targetbranch", StringComparison.OrdinalIgnoreCase))
                {
                    return Models.Branch.Parse(e.Value.GetString());
                }
            }

            throw new ArgumentException($"Could not determine pull request target branch for build {build.Id} because build parameters is in an unknown format");
        }

        // For all other build reasons, the target branch is the same as the source branch.

        if (string.IsNullOrEmpty(build.SourceBranch))
        {
            throw new ArgumentException($"Could not determine target branch for build {build.Id} because it is not a pull request and SourceBranch is missing");
        }

        if (Models.GitRef.Parse(build.SourceBranch) is Models.Branch branch)
        {
            return branch;
        }

        return null;
    }


    public static string GetPullRequestUrl(Build build)
    {
        if (build.Reason == BuildReason.PullRequest)
        {
            if (build.Parameters == null)
            {
                return null;
            }

            string pullRequestNumber = "";
            string sourceRepositoryUri = "";
            var props = JsonSerializer.Deserialize<JsonElement>(build.Parameters);
            foreach (JsonProperty e in props.EnumerateObject())
            {
                if (e.Name.Equals("system.pullrequest.pullRequestNumber", StringComparison.OrdinalIgnoreCase))
                {
                    pullRequestNumber = e.Value.GetString();
                }

                if (e.Name.Equals("system.pullrequest.sourceRepositoryUri", StringComparison.OrdinalIgnoreCase))
                {
                    sourceRepositoryUri = e.Value.GetString();
                }
            }

            if (!string.IsNullOrEmpty(pullRequestNumber) && !string.IsNullOrEmpty(sourceRepositoryUri))
            {
                return $"{sourceRepositoryUri}/pull/{pullRequestNumber}";
            }

        }

        return null;
    }

    private static Models.BuildResult MapModel(BuildResult result)
    {
        return result switch
        {
            BuildResult.None => Models.BuildResult.None,
            BuildResult.Succeeded => Models.BuildResult.Succeeded,
            BuildResult.PartiallySucceeded => Models.BuildResult.PartiallySucceeded,
            BuildResult.Failed => Models.BuildResult.Failed,
            BuildResult.Canceled => Models.BuildResult.Canceled,
            _ => throw new ArgumentOutOfRangeException(
                nameof(result),
                $"BuildResult.{result} does not map to known value"
            )
        };
    }

    private static Models.ResultGroupType MapModel(ResultGroupType result)
    {
        return result switch
        {
            ResultGroupType.None => Models.ResultGroupType.None,
            ResultGroupType.DataDriven => Models.ResultGroupType.DataDriven,
            ResultGroupType.Generic => Models.ResultGroupType.Generic,
            ResultGroupType.OrderedTest => Models.ResultGroupType.OrderedTest,
            ResultGroupType.Rerun => Models.ResultGroupType.Rerun,
            _ => throw new ArgumentOutOfRangeException(
                nameof(result),
                $"ResultGroupType.{result} does not map to known value"
            )
        };
    }

    private static ResultDetails MapModel(Models.ResultDetails result)
    {
        return result switch
        {
            Models.ResultDetails.None => ResultDetails.None,
            Models.ResultDetails.Iterations => ResultDetails.Iterations,
            Models.ResultDetails.WorkItems => ResultDetails.WorkItems,
            Models.ResultDetails.SubResults => ResultDetails.SubResults,
            Models.ResultDetails.Point => ResultDetails.Point,
            _ => throw new ArgumentOutOfRangeException(
                nameof(result),
                $"ResultDetails.{result} does not map to known value"
            )
        };
    }

    private static Models.TestCaseResult MapModel(TestCaseResult r, int? buildId = default, string projectName = default, int? runId = default)
    {
        var isRerun = r.ResultGroupType == ResultGroupType.Rerun;
        var outcome = isRerun ? Models.TestOutcomeValue.PassedOnRerun : Models.TestOutcomeValue.Parse(r.Outcome);

        return new Models.TestCaseResult(
            r.AutomatedTestName,
            r.CompletedDate,
            outcome,
            runId ?? int.Parse(r.TestRun.Id),
            r.Id,
            buildId ?? r.BuildReference?.Id ?? int.Parse(r.Build.Id),
        MapModel(r.FailingSince),
            HttpUtility.HtmlDecode(r.ErrorMessage),
            HttpUtility.HtmlDecode(r.StackTrace),
            projectName ?? r.Project.Name,
            HttpUtility.HtmlDecode(r.Comment),
            r.DurationInMs,
            1,
            MapModel(r.ResultGroupType),
            r.SubResults?.Select(s => MapModel(r, s, buildId, projectName, runId)).ToImmutableList(),
            r.SubResults?.Count(s => s.Outcome == "Failed") ?? 1,
            r.SubResults?.Count ?? 1
        );
    }

    private static Models.PreviousBuildRef MapModel(FailingSince f)
    {
        if (f == null)
            return null;

        return new Models.PreviousBuildRef(
            buildNumber: f.Build.Number,
            date: f.Date
        );
    }

    private static Models.TestCaseResult MapModel(TestCaseResult root, TestSubResult r, int? buildId = default, string projectName = default, int? runId = default)
    {
        var isRerun = r.ResultGroupType == ResultGroupType.Rerun;
        var outcome = isRerun ? Models.TestOutcomeValue.PassedOnRerun : Models.TestOutcomeValue.Parse(r.Outcome);
        var attemptObj = r.CustomFields?.FirstOrDefault(r => r.FieldName == "AttemptId")?.Value;
        int attempt = attemptObj != null ? Convert.ToInt32(attemptObj) : 1;

        return new Models.TestCaseResult(
            r.DisplayName,
            root.CompletedDate,
            outcome,
            runId ?? int.Parse(root.TestRun.Id),
            root.Id,
            buildId ?? root.BuildReference?.Id ?? int.Parse(root.Build.Id),
            MapModel(root.FailingSince),
            r.ErrorMessage,
            r.StackTrace,
            projectName ?? root.Project.Name,
            r.Comment,
            r.DurationInMs,
            attempt,
            MapModel(r.ResultGroupType),
            r.SubResults?.Select(s => MapModel(root, s, buildId, projectName, runId)).ToImmutableList(),
            r.SubResults?.Count(s => s.Outcome == "Failed") ?? 1,
            r.SubResults?.Count ?? 1
        );
    }

    private static Models.TestRunSummary MapModel(TestRun r)
    {
        return new Models.TestRunSummary(r.Id, r.Name, MapModel(r.PipelineReference));
    }

    private static Models.TimelineRecord MapModel(TimelineRecord r)
    {
        return new Models.TimelineRecord(
            id: r.Id,
            parentId: r.ParentId,
            result: MapModel(r.Result),
            issues: r.Issues.Select(MapModel).ToImmutableList(),
            recordType: MapModelRecordType(r.RecordType),
            attempt: r.Attempt,
            name: r.Name,
            identifier: r.Identifier,
            logUrl: r.Log?.Url,
            previousAttempts: r.PreviousAttempts.Select(MapModel).ToImmutableList(),
            order: r.Order,
            startDate: r.StartTime
        );
    }

    private static Models.TimelineIssue MapModel(Issue r)
    {
        return new Models.TimelineIssue(
            message: r.Message,
            MapModel(r.Type),
            data: r.Data.ToImmutableDictionary()
        );
    }

    private static Models.TimelineAttempt MapModel(TimelineAttempt r)
    {
        return new Models.TimelineAttempt(
            attempt: r.Attempt,
            timelineId: r.TimelineId
        );
    }

    private static Models.RecordType MapModelRecordType(string recordType)
    {
        return recordType switch
        {
            "Job" => Models.RecordType.Job,
            "Task" => Models.RecordType.Task,
            "Phase" => Models.RecordType.Phase,
            "Stage" => Models.RecordType.Stage,
            _ => Models.RecordType.Other
        };
    }

    private static Models.TaskResult MapModel(TaskResult? r)
    {
        if (!r.HasValue)
            return Models.TaskResult.None;
        return r.Value switch
        {
            TaskResult.Succeeded => Models.TaskResult.Succeeded,
            TaskResult.SucceededWithIssues => Models.TaskResult.SucceededWithIssues,
            TaskResult.Failed => Models.TaskResult.Failed,
            TaskResult.Canceled => Models.TaskResult.Canceled,
            TaskResult.Skipped => Models.TaskResult.Skipped,
            TaskResult.Abandoned => Models.TaskResult.Abandoned,
            _ => throw new ArgumentOutOfRangeException(nameof(r), $"Could not map TaskResult.{r.Value} to model")
        };
    }

    private static Models.IssueType MapModel(IssueType i)
    {
        return i switch
        {
            IssueType.Error => Models.IssueType.Error,
            IssueType.Warning => Models.IssueType.Warning,
            _ => throw new ArgumentOutOfRangeException(nameof(i), $"Could not map IssueType.{i} to model")
        };
    }

    private static Models.BuildValidationResult MapModel(BuildRequestValidationResult result)
    {
        return new Models.BuildValidationResult(
            result.Result switch
            {
                ValidationResult.OK => Models.BuildValidationStatus.Ok,
                ValidationResult.Warning => Models.BuildValidationStatus.Warning,
                ValidationResult.Error => Models.BuildValidationStatus.Error,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(result),
                    $"ValidationResult.{result.Result} does not map to known value"
                )
            },
            result.Message
        );
    }

    private Models.Build MapModel(Build build)
    {
        ImmutableDictionary<string, string> MapLinks(IReadOnlyDictionary<string, object> linkMap)
        {
            ImmutableDictionary<string, string>.Builder builder =
                ImmutableDictionary.CreateBuilder<string, string>();
            foreach ((string name, object linkObj) in linkMap)
            {
                string href = (linkObj as ReferenceLink)?.Href;
                if (string.IsNullOrEmpty(href))
                {
                    _logger.LogWarning(
                        "Link {link} for build {buildId} is not a valid ReferenceLink with Href, it is of type {linkType}",
                        name,
                        build.Id,
                        linkObj?.GetType().FullName ?? "null"
                    );
                }
                else
                {
                    builder.Add(name, href);
                }
            }

            return builder.ToImmutable();
        }

        if (!build.TriggerInfo.TryGetValue("pr.sourceSha", out string prSourceSha))
        {
            prSourceSha = build.SourceVersion;
        }

        if (!build.TriggerInfo.TryGetValue("pr.number", out string prNumber))
        {
            prNumber = null;
        }

        string organization = BuildUrlUtils.ParseOrganizationFromBuildUrl(build.Url);

        return new Models.Build(
            build.Id,
            build.BuildNumber,
            BuildUrlUtils.GetBuildUrl(organization, build.Project.Name, build.Id),
            build.Project.Name,
            build.Project.Id.ToString(),
            build.Definition.Name,
            build.Definition.Id,
            MapModel(build.Result ?? BuildResult.None),
            build.Repository.Name ?? build.Repository.Id,
            build.Repository.Type,
            prSourceSha,
            prNumber,
            GetPullRequestUrl(build),
            GetTargetBranch(build),
            MapLinks(build.Links.Links),
            build.ValidationResults.Select(MapModel).ToImmutableList(),
            build.FinishTime,
            build.Status == BuildStatus.Completed,
            build.Reason.ToString(),
            organization
        );
    }

    private static Models.PipelineReference MapModel(PipelineReference p)
    {
        return new Models.PipelineReference(p.PipelineId, MapModel(p.StageReference), MapModel(p.PhaseReference), MapModel(p.JobReference));
    }

    private static Models.StageReference MapModel(StageReference s)
    {
        return new Models.StageReference(s.StageName, s.Attempt);
    }

    private static Models.PhaseReference MapModel(PhaseReference p)
    {
        return new Models.PhaseReference(p.PhaseName, p.Attempt);
    }

    private static Models.JobReference MapModel(JobReference j)
    {
        return new Models.JobReference(j.JobName, j.Attempt);
    }

    private static Models.HelixMetadata MapModel(HelixMetadataContract helixMetaDataJson)
    {
        return new Models.HelixMetadata(
            ImmutableList<Models.TestCaseResult>.Empty,
            helixMetaDataJson.TestLists,
            helixMetaDataJson.Partitions,
            helixMetaDataJson.ResultCounts
        );
    }

    public async Task<Stream> GetLogContent(string orgId, string project, int buildId, int logId)
    {
        using var connection = _connections.GetConnection(orgId);
        BuildHttpClient buildClient = connection.GetClient<BuildHttpClient>();
        return await buildClient.GetBuildLogAsync(project, buildId, logId);
    }

    public async Task<IReadOnlyList<Models.Build>> GetFailedBuildsAsync(string orgId, string projectId, string repository, CancellationToken cancellationToken)
    {
        using var connection = _connections.GetConnection(orgId);
        BuildHttpClient buildClient = connection.GetClient<BuildHttpClient>();
        var buildList = await buildClient.GetBuildsAsync(project: projectId, repositoryId: repository, resultFilter: BuildResult.Failed, repositoryType: "Github", minFinishTime: DateTime.Now.AddDays(-1));

        return buildList.Select(MapModel).ToList();
    }

    [DataContract]
    private class HelixMetadataContract
    {
        [DataMember(Name = "version", EmitDefaultValue = false, IsRequired = true, Order = 0)]
        public int Version { get; set; }

        [DataMember(Name = "rerun_tests", EmitDefaultValue = false, IsRequired = false, Order = 1)]
        public TestCaseResult[] RerunTests { get; set; }

        [DataMember(Name = "test_lists", EmitDefaultValue = false, IsRequired = false, Order = 2)]
        public Dictionary<string, string> TestLists { get; set; }

        [DataMember(Name = "partitions", EmitDefaultValue = false, IsRequired = false, Order = 3)]
        public int Partitions { get; set; }

        [DataMember(Name = "result_counts", EmitDefaultValue = false, IsRequired = false, Order = 4)]
        public Dictionary<string, int> ResultCounts { get; set; }
    }
}
