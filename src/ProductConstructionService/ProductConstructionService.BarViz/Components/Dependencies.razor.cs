// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Components;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using ProductConstructionService.BarViz.Code.Helpers;

namespace ProductConstructionService.BarViz.Components;

public partial class Dependencies
{
    [Inject]
    public required IProductConstructionServiceApi PcsApi { get; set; }

    [Parameter]
    public int ChannelId { get; set; }

    [Parameter]
    public int BuildId { get; set; }

    private bool _includeReleasedBuilds;

    private bool _showSubDependencies;

    private bool _includeToolset;

    private BuildGraphData? _buildGraphData;

    protected override async Task OnParametersSetAsync()
    {
        _buildGraphData = null;
        StateHasChanged();

        BuildGraph buildGraph = await PcsApi.Builds.GetBuildGraphAsync(BuildId);
        _buildGraphData = new BuildGraphData(buildGraph, BuildId, ChannelId);
    }
}
