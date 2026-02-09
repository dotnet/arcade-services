// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using System.Threading.Tasks;
using BuildInsights.BuildAnalysis.Models;

namespace BuildInsights.BuildAnalysis.Services;

public interface IPipelineRequestedService
{
    Task<bool> IsBuildPipelineRequested(string repositoryId, string targetBranch, int definitionId, int buildId);
    Task<BuildsByPipelineConfiguration> GetBuildsByPipelineConfiguration(ImmutableList<BuildReferenceIdentifier> relatedBuilds, NamedBuildReference buildReference);
}
