// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.BuildAnalysis.Services;
using BuildInsights.Data;
using BuildInsights.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BuildInsights.BuildAnalysis.Models;

public class BuildProcessingStatusStatusProvider : IBuildProcessingStatusService
{
    private readonly BuildInsightsContext _context;

    public BuildProcessingStatusStatusProvider(BuildInsightsContext context)
    {
        _context = context;
    }

    public async Task<bool> IsBuildBeingProcessed(DateTimeOffset since, string repository, int buildId, CancellationToken cancellationToken)
    {
        return await _context.BuildProcessingStatusEvents.AnyAsync(
            e =>
                e.Repository == NormalizeRepository(repository)
                && e.BuildId == buildId
                && e.Status == BuildProcessingStatus.InProcess
                && e.Timestamp > since,
            cancellationToken: cancellationToken);
    }

    public async Task SaveBuildAnalysisProcessingStatus(string repository, int buildId, BuildProcessingStatus processingStatus)
    {
        var buildEvent = new BuildProcessingStatusEvent
        {
            Repository = NormalizeRepository(repository),
            BuildId = buildId,
            Status = processingStatus,
        };

        await _context.BuildProcessingStatusEvents.AddAsync(buildEvent);
        await _context.SaveChangesAsync();
    }

    public async Task SaveBuildAnalysisProcessingStatus(List<(string repository, int buildId)> builds, BuildProcessingStatus processingStatus)
    {
        IEnumerable<BuildProcessingStatusEvent> events = builds.Select(build =>
            new BuildProcessingStatusEvent
            {
                Repository = NormalizeRepository(build.repository),
                BuildId = build.buildId,
                Status = processingStatus,
            });

        await _context.BuildProcessingStatusEvents.AddRangeAsync(events);
        await _context.SaveChangesAsync();
    }

    public async Task<List<BuildProcessingStatusEvent>> GetBuildsWithOverrideConclusion(DateTimeOffset since, CancellationToken cancellationToken)
    {
        return await _context.BuildProcessingStatusEvents
            .Where(e => e.Timestamp > since && e.Status == BuildProcessingStatus.ConclusionOverridenByUser)
            .ToListAsync(cancellationToken);
    }


    private static string NormalizeRepository(string repository)
    {
        return $"{repository.Replace('/', '.')}";
    }
}
