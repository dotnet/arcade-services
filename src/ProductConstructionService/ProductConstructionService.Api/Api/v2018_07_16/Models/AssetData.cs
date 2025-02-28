// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel.DataAnnotations;

#nullable disable
namespace ProductConstructionService.Api.v2018_07_16.Models;

public class AssetData
{
    [StringLength(250)]
    public string Name { get; set; }

    [StringLength(75)]
    public string Version { get; set; }

    public bool NonShipping { get; set; }

    public List<AssetLocationData> Locations { get; set; }

    public Maestro.Data.Models.Asset ToDb()
    {
        return new Maestro.Data.Models.Asset
        {
            Name = Name,
            Version = Version,
            Locations = Locations?.Select(l => l.ToDb()).ToList() ?? [],
            NonShipping = NonShipping
        };
    }
}
