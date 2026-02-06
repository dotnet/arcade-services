using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Models;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Services;

public interface IBuildProcessingStatusService
{
    Task<bool> IsBuildBeingProcessed(DateTimeOffset since, string repository, int buildId, CancellationToken cancellationToken);
    Task SaveBuildAnalysisProcessingStatus(string repository, int buildId, BuildProcessingStatus processingStatus);
    Task SaveBuildAnalysisProcessingStatus(List<(string repository, int buildId)> builds, BuildProcessingStatus processingStatus);
    Task<List<BuildProcessingStatusEvent>> GetBuildsWithOverrideConclusion(DateTimeOffset since, CancellationToken cancellationToken);
}
