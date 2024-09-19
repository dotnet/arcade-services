// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Components;
using ProductConstructionService.BarViz.Code.Helpers;
using ProductConstructionService.Client;
using ProductConstructionService.Client.Models;

namespace ProductConstructionService.BarViz.Components;


public partial class DependencyGrid
{
    [Inject]
    public required IProductConstructionServiceApi PcsApi { get; set; }

    [Parameter]
    public int ChannelId { get; set; }

    [Parameter]
    public int BuildId { get; set; }

    private bool _includeReleasedBuilds;

    private bool IncludeReleasedBuilds
    {
        get => _includeReleasedBuilds;
        set
        {
            _includeReleasedBuilds = value;
            UpdateDataSource();
        }
    }

    private bool _showSubDependencies;

    private bool ShowSubDependencies
    {
        get => _showSubDependencies;
        set
        {
            _showSubDependencies = value;
            UpdateDataSource();
        }
    }

    private bool _includeToolset;

    private bool IncludeToolset
    {
        get => _includeToolset;
        set
        {
            _includeToolset = value;
            UpdateDataSource();
        }
    }

    private BuildGraphData? _buildGraphData;
    private IQueryable<BuildDependenciesGridRow>? _dependenciesGridData;

    protected void FilterSwitchChanged(bool value)
    {
        UpdateDataSource();
        StateHasChanged();
    }

    protected override async Task OnParametersSetAsync()
    {
        _buildGraphData = null;
        StateHasChanged();

        BuildGraph buildGraph = await PcsApi.Builds.GetBuildGraphAsync(BuildId);
        _buildGraphData = new BuildGraphData(buildGraph, BuildId, ChannelId);
        UpdateDataSource();
    }

    private void UpdateDataSource()
    {
        if (_buildGraphData != null)
        {
            _dependenciesGridData = _buildGraphData.BuildDependenciesGridData(IncludeReleasedBuilds, ShowSubDependencies, IncludeToolset).AsQueryable();
        }
        else
        {
            _dependenciesGridData = null;
        }
    }
}
