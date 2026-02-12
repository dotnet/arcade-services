// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BuildInsights.BuildAnalysis.Models;
using BuildInsights.GitHub.Models;
using BuildInsights.GitHub;

namespace BuildInsights.BuildAnalysis;

public interface IRelatedBuildService
{
    Task<RelatedBuilds> GetRelatedBuilds(BuildReferenceIdentifier singleBuild, CancellationToken cancellationToken);
    Task<Build> GetRelatedBuildFromCheckRun(string repository, string sourceSha);
}

public class RelatedBuildProvider : IRelatedBuildService
{
    private readonly IGitHubChecksService _gitHubChecksService;
    private readonly ILogger<RelatedBuildProvider> _logger;
    private readonly List<string> _allowedTargetProjects;
    private readonly IPipelineRequestedService _pipelineRequestedService;
    private readonly IBuildDataService _buildDataService;

    public RelatedBuildProvider(
        IGitHubChecksService gitHubChecksService,
        IOptions<RelatedBuildProviderSettings> relatedBuildProviderSetting,
        IPipelineRequestedService pipelineRequestedService,
        IBuildDataService buildDataService,
        ILogger<RelatedBuildProvider> logger)
    {
        _gitHubChecksService = gitHubChecksService;
        _allowedTargetProjects = relatedBuildProviderSetting.Value.AllowedTargetProjects.Values.ToList();
        _pipelineRequestedService = pipelineRequestedService;
        _buildDataService = buildDataService;
        _logger = logger;
    }

    public async Task<RelatedBuilds> GetRelatedBuilds(
        BuildReferenceIdentifier singleBuild,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching builds related to build: {organization}/{project}/{buildId}", singleBuild.Org, singleBuild.Project, singleBuild.BuildId);

        IEnumerable<CheckRun> checkRuns = await _gitHubChecksService.GetBuildCheckRunsAsync(singleBuild.RepositoryId, singleBuild.SourceSha);

        var related = new List<BuildReferenceIdentifier>();

        foreach(CheckRun run in checkRuns)
        {
            if (!_allowedTargetProjects.Contains(run.AzureDevOpsProjectId))
            {
                _logger.LogWarning("Parsed project ID {runProject} is not a supported Azure DevOps project, skipping", run.AzureDevOpsProjectId);
                continue;
            }

            if (run.AzureDevOpsBuildId != singleBuild.BuildId)
            {
                _logger.LogInformation("Found related build build: {relatedBuildId}", run.AzureDevOpsBuildId);

                related.Add(
                    new BuildReferenceIdentifier(
                        run.Organization,
                        run.AzureDevOpsProjectId,
                        run.AzureDevOpsBuildId,
                        run.AzureDevOpsBuildUrl,
                        run.AzureDevOpsPipelineId,
                        run.AzureDevOpsPipelineName,
                        singleBuild.RepositoryId,
                        singleBuild.SourceSha,
                        singleBuild.TargetBranch,
                        run.Status == CheckStatus.Completed
                    )
                );
            }

            if(run.Status != CheckStatus.Completed)
            {
                _logger.LogInformation($"Build ID '{run.AzureDevOpsBuildId}' on pipeline ID '{run.AzureDevOpsPipelineId}' has not completed.");
            }
        }

        return new RelatedBuilds(related);
    }

    public async Task<Build> GetRelatedBuildFromCheckRun(string repository, string sourceSha)
    {
        IEnumerable<CheckRun> checkRuns = await _gitHubChecksService.GetBuildCheckRunsAsync(repository, sourceSha);

        foreach (CheckRun run in checkRuns)
        {
            if (!_allowedTargetProjects.Contains(run.AzureDevOpsProjectId))
            {
                _logger.LogWarning("Parsed project ID {runProject} is not a supported Azure DevOps project, skipping", run.AzureDevOpsProjectId);
                continue;
            }
            
            _logger.LogInformation("Found related build: {relatedBuildId}", run.AzureDevOpsBuildId);


            Build build = await _buildDataService.GetBuildAsync(run.Organization, run.AzureDevOpsProjectId, run.AzureDevOpsBuildId, CancellationToken.None);
            if (await _pipelineRequestedService.IsBuildPipelineRequested(build.Repository.Name, build.TargetBranch.BranchName, build.DefinitionId, build.Id))
            {
                return build;
            }
        }

        return null;
    }
}
