// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using System.Text.Json;
using BuildInsights.BuildAnalysis.Models;
using BuildInsights.GitHub;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildInsights.BuildAnalysis;

public interface IPipelineRequestedService
{
    Task<bool> IsBuildPipelineRequested(string repositoryId, string targetBranch, int definitionId, int buildId);
    Task<BuildsByPipelineConfiguration> GetBuildsByPipelineConfiguration(ImmutableList<BuildReferenceIdentifier> relatedBuilds, NamedBuildReference buildReference);
}

public class PipelineRequestedProvider : IPipelineRequestedService
{
    private readonly BuildAnalysisFileSettings _buildAnalysisFileSettings;
    private readonly IGitHubRepositoryService _githubRepositoryService;
    private readonly ILogger<PipelineRequestedProvider> _logger;

    public PipelineRequestedProvider(IGitHubRepositoryService githubRepositoryService,
        IOptions<BuildAnalysisFileSettings> buildAnalysisFileSettings,
        ILogger<PipelineRequestedProvider> logger)
    {
        _githubRepositoryService = githubRepositoryService;
        _buildAnalysisFileSettings = buildAnalysisFileSettings.Value;
        _logger = logger;
    }

    public async Task<bool> IsBuildPipelineRequested(string repositoryId, string targetBranch, int definitionId, int buildId)
    {
        BuildAnalysisRepositorySettings buildAnalysisSettings = await GetBuildAnalysisRepositorySettings(repositoryId, targetBranch);

        if (!buildAnalysisSettings.FilterPipelines || buildAnalysisSettings.PipelinesToAnalyze.Any(p => p.PipelineId == definitionId))
        {
            return true;
        }

        _logger.LogInformation("Skipping analysis of build:{buildId} because it was filter by build analysis settings of repository: {repository} in branch: {branch}",
        buildId, repositoryId, targetBranch);
        return false;
    }

    public async Task<BuildsByPipelineConfiguration> GetBuildsByPipelineConfiguration(ImmutableList<BuildReferenceIdentifier> relatedBuilds, NamedBuildReference build)
    {
        BuildAnalysisRepositorySettings buildAnalysisSettings = await GetBuildAnalysisRepositorySettings(build.RepositoryId, build.TargetBranch);

        if (buildAnalysisSettings.FilterPipelines)
        {
            List<BuildReferenceIdentifier> includedPipelinesBuilds = relatedBuilds.Where(b => buildAnalysisSettings.PipelinesToAnalyze.Any(p => p.PipelineId == b.DefinitionId)).ToList();
            List<BuildReferenceIdentifier> filteredPipelinesBuilds = relatedBuilds.Where(b => buildAnalysisSettings.PipelinesToAnalyze.All(p => p.PipelineId != b.DefinitionId)).ToList();

            _logger.LogInformation("Included pipelines: {includedPipelinesCount} / Filtered pipelines: {filteredPipelinesCount}  for build {buildId} in repository {repository} for branch: {targetBranch}",
                includedPipelinesBuilds.Count, filteredPipelinesBuilds.Count, build.BuildId, build.RepositoryId, build.TargetBranch);
            return new BuildsByPipelineConfiguration(includedPipelinesBuilds.ToImmutableList(), filteredPipelinesBuilds.ToImmutableList());
        }

        _logger.LogInformation("Build {buildId} in repository {repository} for branch: {targetBranch} doesn't have filtered settings",
            build.BuildId, build.RepositoryId, build.TargetBranch);

        return new BuildsByPipelineConfiguration(relatedBuilds, []);
    }

    private async Task<BuildAnalysisRepositorySettings> GetBuildAnalysisRepositorySettings(string repositoryId, string targetBranch)
    {
        string buildAnalysisFile = await _githubRepositoryService.GetFileAsync(repositoryId, _buildAnalysisFileSettings.FilePath, targetBranch);
        try
        {
            BuildAnalysisRepositorySettings buildAnalysisSettings = !string.IsNullOrEmpty(buildAnalysisFile)
                ? JsonSerializer.Deserialize<BuildAnalysisRepositorySettings>(buildAnalysisFile)!
                : new BuildAnalysisRepositorySettings();

            return buildAnalysisSettings;
        }
        catch
        {
            _logger.LogInformation("Unable to process build analysis file settings of repository: {repository} in branch: {branch}, processing all pipelines instead",
                repositoryId, targetBranch);

            return new BuildAnalysisRepositorySettings();
        }
    }
}
