// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using BuildInsights.BuildAnalysis.Models;

namespace BuildInsights.BuildAnalysis.Services;

public interface IBuildDataService
{
    Task<Build> GetBuildAsync(string orgId, string projectId, int buildId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Build>> GetFailedBuildsAsync(string orgId, string projectId, string repository, CancellationToken cancellationToken);
    Task<List<TestRunDetails>> GetFailingTestsForBuildAsync(
        Build build,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<TestRunDetails>> GetAllFailingTestsForBuildAsync(
        Build build,
        CancellationToken cancellationToken);
    Task<List<TestHistoryByBranch>> GetTestHistoryAsync(
        string orgId,
        string projectId,
        string testName,
        DateTimeOffset maxCompleted,
        CancellationToken cancellationToken);
    Task<TestCaseResult> GetTestResultByIdAsync(string orgId, string projectId, int testRunId, int testCaseResultId, CancellationToken cancellationToken, ResultDetails resultDetails = ResultDetails.None);
    Task<IReadOnlyList<Build>> GetLatestBuildsForBranchAsync(string orgId, string projectId, int definitionId, GitRef targetBranch, DateTimeOffset latestDate, CancellationToken cancellationToken);
    Task<IReadOnlyList<TimelineRecord>> GetLatestBuildTimelineRecordsAsync(string orgId, string projectId, int buildId, CancellationToken cancellationToken);
    Task<IReadOnlyList<TimelineRecord>> GetBuildTimelineRecordsAsync(string orgId, string projectId, int buildId, Guid timelineId, CancellationToken cancellationToken);
    Task<IReadOnlyList<TimelineRecord>> GetTimelineRecordsFromAllAttempts(IReadOnlyCollection<TimelineRecord> latestTimelineRecords, string orgId, string projectId, int buildId, CancellationToken cancellationToken);
    Task<BuildConfiguration> GetBuildConfiguration(string orgId, string projectId, int buildId, string artifactName, string fileName, CancellationToken cancellationToken);
    Task<string> GetProjectName(string orgId, string projectId);

    Task<ImmutableList<TestRunDetails>> GetTestsForBuildAsync(
        Build build,
        CancellationToken cancellationToken);

    IAsyncEnumerable<HelixMetadata> GetTestRunMetaDataAsync(
        ImmutableList<string> attachmentUrl,
        CancellationToken cancellationToken);
    Task<Stream> GetLogContent(string orgId, string project, int buildId, int logId);
}
