// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.BuildAnalysis.Models;
using BuildInsights.BuildAnalysis.WorkItems.Models;
using Microsoft.Extensions.Logging;
using ProductConstructionService.WorkItems;

namespace BuildInsights.BuildAnalysis.WorkItems.Processors;

public class CheckRunRerunEventProcessor : WorkItemProcessor<CheckRunRerunGitHubEvent>
{
    private readonly IBuildAnalyzer _buildAnalyzer;
    private readonly IRelatedBuildService _relatedBuildService;
    private readonly ILogger<CheckRunRerunEventProcessor> _logger;

    public CheckRunRerunEventProcessor(
        IBuildAnalyzer buildAnalyzer,
        IRelatedBuildService relatedBuildService,
        ILogger<CheckRunRerunEventProcessor> logger)
    {
        _buildAnalyzer = buildAnalyzer;
        _relatedBuildService = relatedBuildService;
        _logger = logger;
    }

    public override async Task<bool> ProcessWorkItemAsync(
        CheckRunRerunGitHubEvent workItem,
        CancellationToken cancellationToken)
    {
        Build relatedBuild = await _relatedBuildService.GetRelatedBuildFromCheckRun(
            workItem.Repository,
            workItem.HeadSha);

        if (relatedBuild == null)
        {
            _logger.LogInformation("No authorized related build found for rerun of commit {commit} on repository {repository}",
                workItem.HeadSha,
                workItem.Repository);
            return false;
        }

        await _buildAnalyzer.AnalyzeBuild(
            relatedBuild.OrganizationName,
            relatedBuild.ProjectId,
            relatedBuild.Id,
            workItem.QueuedAt,
            cancellationToken);

        return true;
    }
}
