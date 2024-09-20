// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using System.Text;
using Microsoft.FluentUI.AspNetCore.Components;
using ProductConstructionService.Client.Models;

namespace ProductConstructionService.BarViz.Code.Helpers;


public class BuildGraphData
{
    private readonly BuildGraph _buildGraph;
    private readonly Build _rootBuild;
    private int _channelId;

    public BuildGraphData(BuildGraph buildGraph, int rootBuildId, int channelId)
    {
        _buildGraph = buildGraph;
        if (buildGraph.Builds.TryGetValue(rootBuildId.ToString(CultureInfo.InvariantCulture), out var rb))
        {
            _rootBuild = rb;
        }
        else
        {
            throw new ArgumentException($"Reference build with id {rootBuildId} not found in build graph");
        }

        _channelId = channelId;
    }

    public List<BuildDependenciesGridRow> BuildDependenciesGridData(bool includeReleasedBuilds, bool showSubDependencies, bool includeToolset)
    {
        var buildGraphData = new List<BuildDependenciesGridRow>();
        var graphBuilds = new Dictionary<int, Build>();

        void AddBuidToGrid(Build build, int level)
        {
            // do not add the same build twice
            if (graphBuilds.ContainsKey(build.Id))
            {
                return;
            }
            var buildGridRow = new BuildDependenciesGridRow
            {
                BuildId = build.Id,
                RepositoryUrl = build.GetRepoUrl(),
                RepositoryName = build.GetRepoName(),
                BuildNumber = build.AzureDevOpsBuildNumber,
                BuildStaleness = build.GetBuildStalenessText(),
                Commit = build.Commit,
                CommitShort = build.Commit.Substring(0, 7),
                CommitLink = build.GetCommitLink(),
                BuildUrl = build.GetBuildUrl(),
                AgeDays = build.GetBuildAgeDays(),
                LinkToBuildDetails = build.GetLinkToBuildDetails(_channelId),
                Level = level,
                Released = build.Released
            };

            buildGraphData.Add(buildGridRow);
            graphBuilds.Add(build.Id, build);
        }

        void AddBuildDependencies(Build build, int level)
        {
            foreach (BuildRef dependency in build.Dependencies.Reverse<BuildRef>())
            {
                var depBuildId = dependency.BuildId;
                if (dependency.IsProduct || includeToolset)
                {
                    if (_buildGraph.Builds.TryGetValue(depBuildId.ToString(CultureInfo.InvariantCulture), out var dependentBuild))
                    {
                        if (!dependentBuild.Released || includeReleasedBuilds)
                        {
                            AddBuidToGrid(dependentBuild, level);
                            if (showSubDependencies)
                            {
                                AddBuildDependencies(dependentBuild, level + 1);
                            }
                        }
                    }
                }
            }
        }

        AddBuidToGrid(_rootBuild, 0);
        AddBuildDependencies(_rootBuild, 1);

        return buildGraphData;
    }

    public List<BuildTreeViewItem> BuildDependenciesTreeData(bool includeToolset)
    {
        BuildTreeViewItem BuildTree(Build build, int level)
        {
            if (build.Dependencies.Count == 0)
            {
                return new BuildTreeViewItem
                {
                    Expanded = level <= 2,
                    Build = build
                };
            }

            List<BuildTreeViewItem> items = new List<BuildTreeViewItem>();
            foreach (var dependency in build.Dependencies)
            {
                if (dependency.IsProduct || includeToolset)
                {
                    if (_buildGraph.Builds.TryGetValue(dependency.BuildId.ToString(), out var dependencyBuild))
                    {
                        items.Add(BuildTree(dependencyBuild, level + 1));
                    }
                }
            }

            return new BuildTreeViewItem
            {
                Expanded = level <= 2,
                Items = items,
                Build = build
            };
        }

        return [BuildTree(_rootBuild, 1)];
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
    public string? CommitLink { get; set; }
    public required string BuildUrl { get; set; }
    public string? LinkToBuildDetails { get; set; }
    public int AgeDays { get; set; }
    public int Level { get; set; }
    public bool Released { get; set; }
}
