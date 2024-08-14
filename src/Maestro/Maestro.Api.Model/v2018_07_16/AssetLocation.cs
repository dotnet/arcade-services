// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Maestro.Api.Model.v2018_07_16;

public class AssetLocation
{
    public AssetLocation(Data.Models.AssetLocation other)
    {
        ArgumentNullException.ThrowIfNull(other);

        Id = other.Id;
        Location = other.Location;
        Type = (LocationType)(int)other.Type;
    }

    public int Id { get; }
    public string Location { get; }
    public LocationType Type { get; }
}
