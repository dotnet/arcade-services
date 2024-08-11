// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Api.Model.v2018_07_16;

namespace Maestro.Api.Model.v2020_02_20;

public class AssetAndLocation
{
    public int AssetId { get; set; }

    public string Location { get; set; }

    public LocationType LocationType { get; set; }
}
