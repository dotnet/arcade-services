// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace Maestro.Api.Model.v2018_07_16;

public class AssetData
{
    [StringLength(150)]
    public string Name { get; set; }

    [StringLength(75)]
    public string Version { get; set; }

    public bool NonShipping { get; set; }

    public List<AssetLocationData> Locations { get; set; }

    public Data.Models.Asset ToDb()
    {
        return new Data.Models.Asset
        {
            Name = Name,
            Version = Version,
            Locations = Locations?.Select(l => l.ToDb()).ToList() ?? [],
            NonShipping = NonShipping
        };
    }
}
