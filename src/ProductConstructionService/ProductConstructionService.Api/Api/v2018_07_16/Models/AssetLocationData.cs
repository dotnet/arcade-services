// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable
namespace ProductConstructionService.Api.v2018_07_16.Models;

public class AssetLocationData
{
    public string Location { get; set; }
    public LocationType Type { get; set; }

    public Maestro.Data.Models.AssetLocation ToDb()
    {
        return new Maestro.Data.Models.AssetLocation
        {
            Location = Location,
            Type = (Maestro.Data.Models.LocationType)(int)Type
        };
    }
}
