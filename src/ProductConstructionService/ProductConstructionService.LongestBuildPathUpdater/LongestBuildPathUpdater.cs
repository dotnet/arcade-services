// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data;
using Maestro.Data.Models;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.Logging;

namespace ProductConstructionService.LongestBuildPathUpdater;
public class LongestBuildPathUpdater
{
    private readonly BuildAssetRegistryContext _context;
    private readonly IBasicBarClient _barClient;
    private readonly ILogger<LongestBuildPathUpdater> _logger;

    public LongestBuildPathUpdater(
        BuildAssetRegistryContext context,
        IBasicBarClient barClient,
        ILogger<LongestBuildPathUpdater> logger)
    {
        _context = context;
        _logger = logger;
        _barClient = barClient;
    }

    public async Task UpdateLongestBuildPathAsync(CancellationToken cancellationToken)
    {
        List<Channel> channels = [.. _context.Channels.Select(c => new Channel() { Id = c.Id, Name = c.Name })];
        IReadOnlyList<string> frequencies = new[] { "everyWeek", "twiceDaily", "everyDay", "everyBuild", "none", };

        _logger.LogInformation($"Will update '{channels.Count}' channels");

        foreach (var channel in channels)
        {
            var flowGraph = await _barClient.GetDependencyFlowGraphAsync(
                channel.Id,
                days: 30,
                includeArcade: false,
                includeBuildTimes: true,
                includeDisabledSubscriptions: false,
                includedFrequencies: frequencies);

            // Get the nodes on the longest path and order them by path time so that the
            // contributing repos are in the right order
            List<DependencyFlowNode> longestBuildPathNodes = [.. flowGraph.Nodes
                .Where(n => n.OnLongestBuildPath)
                .OrderByDescending(n => n.BestCasePathTime)];

            if (longestBuildPathNodes.Any())
            {
                var lbp = new LongestBuildPath()
                {
                    ChannelId = channel.Id,
                    BestCaseTimeInMinutes = longestBuildPathNodes.Max(n => n.BestCasePathTime),
                    WorstCaseTimeInMinutes = longestBuildPathNodes.Max(n => n.WorstCasePathTime),
                    ContributingRepositories = string.Join(';', longestBuildPathNodes.Select(n => $"{n.Repository}@{n.Branch}").ToArray()),
                    ReportDate = DateTimeOffset.UtcNow,
                };

                _logger.LogInformation($"Will update {channel.Name} to best case time {lbp.BestCaseTimeInMinutes} and worst case time {lbp.WorstCaseTimeInMinutes}");
                await _context.LongestBuildPaths.AddAsync(lbp);
            }
            else
            {
                _logger.LogInformation($"Will not update {channel.Name} longest build path because no nodes have {nameof(DependencyFlowNode.OnLongestBuildPath)} flag set. Total node count = {flowGraph.Nodes.Count}");
            }
        }

        await _context.SaveChangesAsync();
    }
}
