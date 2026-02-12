// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.BuildAnalysis.Services;

public interface IBuildRetryService
{
    Task<bool> RetryIfSuitable(string orgId, string projectId, int buildId, CancellationToken cancellationToken = default);
    Task<bool> RetryIfKnownIssueSuitable(string orgId, string projectId, int buildId, CancellationToken cancellationToken = default);

}
