// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.Data.Models;

namespace BuildInsights.BuildAnalysis.Services;

public interface IBuildProcessingStatusService
{
    Task<bool> IsBuildBeingProcessed(DateTimeOffset since, string repository, int buildId, CancellationToken cancellationToken);
    Task SaveBuildAnalysisProcessingStatus(string repository, int buildId, BuildProcessingStatus processingStatus);
    Task SaveBuildAnalysisProcessingStatus(List<(string repository, int buildId)> builds, BuildProcessingStatus processingStatus);
    Task<List<BuildProcessingStatusEvent>> GetBuildsWithOverrideConclusion(DateTimeOffset since, CancellationToken cancellationToken);
}
