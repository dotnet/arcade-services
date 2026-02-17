// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.KnownIssues.WorkItems;
using ProductConstructionService.WorkItems;

namespace BuildInsights.BuildAnalysis.WorkItems.Processors;

public class BuildAnalysisProcessor : WorkItemProcessor<BuildAnalysisRequestWorkItem>
{
    private readonly IBuildAnalyzer _buildAnalyzer;

    public BuildAnalysisProcessor(IBuildAnalyzer buildAnalyzer)
    {
        _buildAnalyzer = buildAnalyzer;
    }

    public override async Task<bool> ProcessWorkItemAsync(
        BuildAnalysisRequestWorkItem workItem,
        CancellationToken cancellationToken)
    {
        await _buildAnalyzer.AnalyzeBuild(
            workItem.OrganizationId,
            workItem.ProjectId,
            workItem.BuildId,
            workItem.QueuedAt,
            cancellationToken);

        return true;
    }
}
