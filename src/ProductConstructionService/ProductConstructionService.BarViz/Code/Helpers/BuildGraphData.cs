// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using Microsoft.DotNet.ProductConstructionService.Client.Models;

namespace ProductConstructionService.BarViz.Code.Helpers;


public class BuildGraphData
{
    private readonly BuildGraph _buildGraph;
    private readonly Build _rootBuild;
    private readonly int _channelId;
    private readonly Dictionary<int, HashSet<int>> _parents;
    private readonly Dictionary<int, HashSet<int>> _children;

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

        // fill parents and children dictionaries
        _parents = [];
        _children = [];
        foreach (var build in _buildGraph.Builds.Values)
        {
            foreach (var dep in build.Dependencies)
            {
                int child = dep.BuildId;
                int parent = build.Id;

                if (!_parents.TryGetValue(child, out HashSet<int>? childParents))
                {
                    childParents = [];
                    _parents.Add(child, childParents);
                }
                childParents.Add(parent);

                if (!_children.TryGetValue(parent, out HashSet<int>? parentChildren))
                {
                    parentChildren = [];
                    _children.Add(parent, parentChildren);
                }
                parentChildren.Add(child);
            }
        }
    }

    public List<BuildDependenciesGridRow> BuildDependenciesGridData(bool includeReleasedBuilds, bool showSubDependencies, bool includeToolset)
    {
        var buildGraphData = new List<BuildDependenciesGridRow>();
        var graphBuilds = new Dictionary<int, Build>();
        var buildsCoherency = new Dictionary<string, Build>(StringComparer.OrdinalIgnoreCase);

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

            string repoUrl = build.GetRepoUrl();
            if (!buildsCoherency.ContainsKey(repoUrl))
            {
                buildsCoherency.Add(build.GetRepoUrl(), build);
            }
            else if (buildsCoherency[repoUrl].Commit != build.Commit)
            {
                buildGridRow.ConflictDependency = true;
            }
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
                    Expanded = false,
                    Build = build
                };
            }

            List<BuildTreeViewItem> items = [];
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
                Expanded = true,
                Items = items,
                Build = build
            };
        }

        return [BuildTree(_rootBuild, 1)];
    }

    public void UpdateSelectedRelations(IQueryable<BuildDependenciesGridRow> dependenciesGridData, int buildId)
    {
        Dictionary<int, BuildDependenciesGridRow> gridBuilds = dependenciesGridData.ToDictionary(gr => gr.BuildId);
        gridBuilds[buildId].DependencyRelationType = SelectedDependencyRelationType.Selected;

        var selectedBuild = _buildGraph.Builds[buildId.ToString(CultureInfo.InvariantCulture)];

        // mark method
        var alreadyIterated = new HashSet<int>();
        void MarkAccordingGraphDictionary(Dictionary<int, HashSet<int>> relations, int buildId, int level, bool childrenFlow, bool deep)
        {
            if (alreadyIterated.Contains(buildId))
            {
                return;
            }
            alreadyIterated.Add(buildId);
            if (relations.TryGetValue(buildId, out var nestedBuilds))
            {
                foreach (var nestedBuildId in nestedBuilds)
                {
                    if (_buildGraph.Builds.TryGetValue(nestedBuildId.ToString(CultureInfo.InvariantCulture), out var b))
                    {
                        if (gridBuilds.TryGetValue(nestedBuildId, out var gridBuild))
                        {
                            if (gridBuild.DependencyRelationType == SelectedDependencyRelationType.None)
                            {
                                if (childrenFlow)
                                {
                                    gridBuild.DependencyRelationType = level == 0 ? SelectedDependencyRelationType.Child : SelectedDependencyRelationType.Descendant;
                                }
                                else
                                {
                                    gridBuild.DependencyRelationType = level == 0 ? SelectedDependencyRelationType.Parent : SelectedDependencyRelationType.Ancestor;
                                }
                            }
                            if (deep)
                            {
                                MarkAccordingGraphDictionary(relations, nestedBuildId, level + 1, childrenFlow, deep);
                            }
                            alreadyIterated.Add(nestedBuildId);
                        }
                    }
                }
            }
        }

        // mark parents
        alreadyIterated.Clear();
        MarkAccordingGraphDictionary(_parents, selectedBuild.Id, 0, false, true);

        // mark children
        alreadyIterated.Clear();
        MarkAccordingGraphDictionary(_children, selectedBuild.Id, 0, true, true);

        // mark conflicts
        var selectedRepo = selectedBuild.GetRepoUrl();
        foreach (var build in gridBuilds.Values)
        {
            if (selectedRepo.Equals(build.RepositoryUrl, StringComparison.OrdinalIgnoreCase) && build.BuildId != buildId)
            {
                build.DependencyRelationType = SelectedDependencyRelationType.Conflict;
            }
        }
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
    public bool ConflictDependency { get; set; }
    public SelectedDependencyRelationType DependencyRelationType { get; set; }
}
