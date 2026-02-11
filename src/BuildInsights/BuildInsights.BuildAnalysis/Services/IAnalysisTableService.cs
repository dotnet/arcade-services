// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using BuildInsights.BuildAnalysis.Models;
using BuildInsights.Data.Models;

namespace BuildInsights.BuildAnalysis.Services;

public interface IBuildAnalysisHistoryService
{
    Task<BuildAnalysisEvent> GetLastBuildAnalysisRecord(int buildId, string definitionName);
    Task SaveBuildAnalysisRecords(ImmutableList<BuildResultAnalysis> completedPipelines, string repositoryId, string project, DateTimeOffset analysisTimestamp);
    Task SaveBuildAnalysisRepositoryNotSupported(string pipeline, int buildId, string repositoryId, string project, DateTimeOffset analysisTimestamp);
    Task<List<BuildAnalysisEvent>> GetBuildsWithRepositoryNotSupported(DateTimeOffset since, CancellationToken cancellationToken);
}
