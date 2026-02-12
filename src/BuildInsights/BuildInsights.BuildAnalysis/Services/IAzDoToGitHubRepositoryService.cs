// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.BuildAnalysis.Models;
using BuildInsights.BuildAnalysis.Providers;

namespace BuildInsights.BuildAnalysis.Services;

public interface IAzDoToGitHubRepositoryService
{
    Task<AzDoToGitHubRepositoryResult> TryGetGitHubRepositorySupportingKnownIssues(BuildRepository buildRepository, string commit);
    Task<bool> IsInternalRepositorySupported(BuildRepository repository, string commit);
}
