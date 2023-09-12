// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Maestro.ContainerApp.Api.Models;

public class AssetLocation
{
    public AssetLocation(Data.Models.AssetLocation other)
    {
        if (other == null)
        {
            throw new ArgumentNullException(nameof(other));
        }

        Id = other.Id;
        Location = other.Location;
        Type = (LocationType) (int) other.Type;
    }

    public int Id { get; }
    public string Location { get; }
    public LocationType Type { get; }
}
