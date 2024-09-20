// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Components;
using ProductConstructionService.BarViz.Code.Helpers;

namespace ProductConstructionService.BarViz.Components;


public partial class DependencyGrid
{
    [Parameter]
    public bool IncludeReleasedBuilds { get; set; }

    [Parameter]
    public bool ShowSubDependencies { get; set; }

    [Parameter]
    public bool IncludeToolset { get; set; }

    [Parameter]
    public BuildGraphData? BuildGraphData { get; set; }

    private IQueryable<BuildDependenciesGridRow>? _dependenciesGridData;

    protected override void OnParametersSet()
    {
        _dependenciesGridData = BuildGraphData != null ?
            BuildGraphData.BuildDependenciesGridData(IncludeReleasedBuilds, ShowSubDependencies, IncludeToolset).AsQueryable() :
            null;
    }
}
