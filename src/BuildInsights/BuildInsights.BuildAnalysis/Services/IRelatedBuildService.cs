// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.BuildAnalysis.Models;

namespace BuildInsights.BuildAnalysis.Services;

public interface IRelatedBuildService
{
    Task<RelatedBuilds> GetRelatedBuilds(BuildReferenceIdentifier singleBuild, CancellationToken cancellationToken);
    Task<Build> GetRelatedBuildFromCheckRun(string repository, string sourceSha);
}
