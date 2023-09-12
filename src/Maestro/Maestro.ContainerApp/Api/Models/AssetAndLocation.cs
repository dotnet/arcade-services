// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Maestro.ContainerApp.Api.Models;

public class AssetAndLocation
{
    public int AssetId { get; set; }

    public string? Location { get; set; }

    public LocationType LocationType { get; set; }
}
