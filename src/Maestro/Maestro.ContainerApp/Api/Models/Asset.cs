// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Maestro.ContainerApp.Api.Models;

public class Asset
{
    public Asset(Data.Models.Asset other)
    {
        if (other == null)
        {
            throw new ArgumentNullException(nameof(other));
        }

        Id = other.Id;
        Name = other.Name;
        Version = other.Version;
        BuildId = other.BuildId;
        NonShipping = other.NonShipping;
        Locations = other.Locations?.Select(al => new AssetLocation(al)).ToList();
    }

    public int Id { get; }

    public string Name { get; }

    public string Version { get; }

    public int BuildId { get; set; }

    public bool NonShipping { get; set; }

    public List<AssetLocation>? Locations { get; }
}
