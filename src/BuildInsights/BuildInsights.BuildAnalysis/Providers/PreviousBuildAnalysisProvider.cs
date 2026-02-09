// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading;
using System.Threading.Tasks;
using BuildInsights.BuildAnalysis.Models;
using BuildInsights.BuildAnalysis.Services;

namespace BuildInsights.BuildAnalysis.Providers;

public class PreviousBuildAnalysisProvider : IPreviousBuildAnalysisService
{
    private readonly IBuildCacheService _cache;

    public PreviousBuildAnalysisProvider(IBuildCacheService cache)
    {
        _cache = cache;
    }

    public Task<BuildResultAnalysis> GetBuildResultAnalysisAsync(BuildReferenceIdentifier buildReference, CancellationToken cancellationToken, bool isValidationAnalysis = false)
    {
        return _cache.TryGetBuildAsync(buildReference, cancellationToken);
    }
}
