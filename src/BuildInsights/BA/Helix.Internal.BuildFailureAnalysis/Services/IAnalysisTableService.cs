using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Models;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Services
{
    public interface IBuildAnalysisHistoryService
    {
        BuildAnalysisEvent GetLastBuildAnalysisRecord(int buildId, string definitionName);
        Task SaveBuildAnalysisRecords(ImmutableList<BuildResultAnalysis> completedPipelines, string repositoryId, string project, DateTimeOffset analysisTimestamp);
        Task SaveBuildAnalysisRepositoryNotSupported(string pipeline, int buildId, string repositoryId, string project, DateTimeOffset analysisTimestamp);
        Task<List<BuildAnalysisEvent>> GetBuildsWithRepositoryNotSupported(DateTimeOffset since, CancellationToken cancellationToken);
    }
}
