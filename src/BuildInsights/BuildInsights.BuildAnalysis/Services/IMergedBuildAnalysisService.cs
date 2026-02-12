// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.BuildAnalysis.Models;

namespace BuildInsights.BuildAnalysis.Services;

public interface IMergedBuildAnalysisService
{
    Task<MergedBuildResultAnalysis> GetMergedAnalysisAsync(
        NamedBuildReference referenceBuild,
        MergeBuildAnalysisAction action,
        CancellationToken cancellationToken);
}

public enum MergeBuildAnalysisAction
{
    Include,
    Exclude,
}
