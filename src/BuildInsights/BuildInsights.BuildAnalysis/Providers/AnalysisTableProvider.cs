// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using BuildInsights.BuildAnalysis.Models;
using BuildInsights.BuildAnalysis.Services;
using BuildInsights.Data;
using BuildInsights.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BuildInsights.BuildAnalysis.Providers;

public class BuildAnalysisHistoryProvider : IBuildAnalysisHistoryService
{
    private readonly BuildInsightsContext _context;

    public BuildAnalysisHistoryProvider(BuildInsightsContext context)
    {
        _context = context;
    }

    public async Task<BuildAnalysisEvent> GetLastBuildAnalysisRecord(int buildId, string definitionName)
    {
        return await _context.BuildAnalysisEvents
            .Where(e => e.PipelineName == definitionName && e.BuildId == buildId)
            .OrderByDescending(e => e.AnalysisTimestamp)
            .FirstOrDefaultAsync();
    }

    public async Task SaveBuildAnalysisRecords(ImmutableList<BuildResultAnalysis> completedPipelines, string repositoryId, string project, DateTimeOffset analysisTimestamp)
    {
        var newEvents = completedPipelines
            .Select(analysis => new BuildAnalysisEvent
            {
                PipelineName = analysis.PipelineName,
                BuildId = analysis.BuildId,
                Repository = repositoryId,
                Project = project,
                AnalysisTimestamp = analysisTimestamp
            });

        await _context.BuildAnalysisEvents.AddRangeAsync(newEvents);
        await _context.SaveChangesAsync();
    }

    public async Task SaveBuildAnalysisRepositoryNotSupported(string pipeline, int buildId, string repositoryId, string project, DateTimeOffset analysisTimestamp)
    {
        var newEvent = new BuildAnalysisEvent
        {
            PipelineName = pipeline,
            BuildId = buildId,
            Repository = repositoryId,
            Project = project,
            AnalysisTimestamp = analysisTimestamp
        };

        await _context.BuildAnalysisEvents.AddAsync(newEvent);
        await _context.SaveChangesAsync();
    }

    public async Task<List<BuildAnalysisEvent>> GetBuildsWithRepositoryNotSupported(DateTimeOffset since, CancellationToken cancellationToken)
    {
        return await _context.BuildAnalysisEvents
            .Where(e => e.IsRepositorySupported == false && e.AnalysisTimestamp > since)
            .ToListAsync(cancellationToken);
    }
}
