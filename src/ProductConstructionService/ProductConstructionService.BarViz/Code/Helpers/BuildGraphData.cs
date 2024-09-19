// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using ProductConstructionService.Client.Models;

namespace ProductConstructionService.BarViz.Code.Helpers;

public class BuildGraphData
{
    private readonly BuildGraph _buildGraph;
    private readonly Build _referenceBuild;
    private int _channelId;

    public BuildGraphData(BuildGraph buildGraph, int referenceBuildId, int channelId)
    {
        _buildGraph = buildGraph;
        if (buildGraph.Builds.TryGetValue(referenceBuildId.ToString(CultureInfo.InvariantCulture), out var rb))
        {
            _referenceBuild = rb;
        }
        else
        {
            throw new ArgumentException($"Reference build with id {referenceBuildId} not found in build graph");
        }

        _channelId = channelId;
    }

    public List<BuildDependenciesGridRow> BuildDependenciesGridData(bool includeReleasedBuilds)
    {
        var buildGraphData = new List<BuildDependenciesGridRow>();

        foreach (var kvp in _buildGraph.Builds.Where(b => (!b.Value.Released || includeReleasedBuilds)))
        {
            Build build = kvp.Value;
            var buildGridRow = new BuildDependenciesGridRow
            {
                BuildId = build.Id,
                RepositoryUrl = build.GetRepoUrl(),
                RepositoryName = build.GetRepoName(),
                BuildNumber = build.AzureDevOpsBuildNumber,
                BuildStaleness = build.GetBuildStaleness(),
                Commit = build.Commit,
                CommitShort = build.Commit.Substring(0, 7),
                BuildUrl = build.GetBuildUrl(),
                AgeDays = build.GetBuildAgeDays(),
                LinkToBuildDetails = build.GetLinkToBuildDetails(_channelId)
            };

            buildGraphData.Add(buildGridRow);
        }
        return buildGraphData;
    }
}

public class BuildDependenciesGridRow
{
    public int BuildId { get; set; }
    public required string RepositoryName { get; set; }
    public required string RepositoryUrl { get; set; }
    public required string BuildNumber { get; set; }
    public required string BuildStaleness { get; set; }
    public required string Commit { get; set; }
    public required string CommitShort { get; set; }
    public required string BuildUrl { get; set; }
    public string? LinkToBuildDetails { get; set; }
    public int AgeDays { get; set; }
}
