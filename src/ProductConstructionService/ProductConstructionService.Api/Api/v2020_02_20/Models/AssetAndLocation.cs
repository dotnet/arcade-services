// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.Api.v2018_07_16.Models;

#nullable disable
namespace ProductConstructionService.Api.v2020_02_20.Models;

public class AssetAndLocation
{
    public int AssetId { get; set; }

    public string Location { get; set; }

    public LocationType LocationType { get; set; }
}
