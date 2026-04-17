// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.BuildAnalysis.Models;
using BuildInsights.BuildAnalysis.WorkItems.Models;
using Maestro.WorkItems;

namespace BuildInsights.BuildAnalysis.WorkItems.Processors;

public class TestBuildAnalysisProcessor : WorkItemProcessor<BuildAnalysisRequestWorkItem>
{
    private readonly IBuildDataService _buildService;
    private readonly IBuildAnalyzer _buildAnalyzer;

    public TestBuildAnalysisProcessor(
        IBuildDataService buildService,
        IBuildAnalyzer buildAnalyzer)
    {
        _buildService = buildService;
        _buildAnalyzer = buildAnalyzer;
    }

    public override async Task<bool> ProcessWorkItemAsync(
        BuildAnalysisRequestWorkItem workItem,
        CancellationToken cancellationToken)
    {
        Build build = await _buildService.GetBuildAsync(
            workItem.OrganizationId,
            workItem.ProjectId,
            workItem.BuildId,
            cancellationToken);

        var buildReference = new NamedBuildReference(
            build.DefinitionName,
            build.Links.Web,
            workItem.OrganizationId,
            workItem.ProjectId,
            workItem.BuildId,
            build.Url,
            build.DefinitionId,
            build.DefinitionName,
            build.Repository.Name, /* TODO - change */
            build.CommitHash,
            build.TargetBranch?.BranchName!);

        await _buildAnalyzer.AnalyzeBuild(
            build,
            buildReference,
            workItem.QueuedAt,
            cancellationToken);

        return true;
    }
}
