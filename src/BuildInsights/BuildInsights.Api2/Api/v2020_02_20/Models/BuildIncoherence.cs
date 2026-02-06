// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable
namespace ProductConstructionService.Api.v2020_02_20.Models;

public class BuildIncoherence
{
    public string Name { get; set; }
    public string Version { get; set; }
    public string Repository { get; set; }
    public string Commit { get; set; }

    public BuildIncoherence(Maestro.Data.Models.BuildIncoherence other)
    {
        if (other == null)
        {
            return;
        }

        Name = other.Name;
        Version = other.Version;
        Repository = other.Repository;
        Commit = other.Commit;
    }

    public Maestro.Data.Models.BuildIncoherence ToDb()
    {
        return new Maestro.Data.Models.BuildIncoherence()
        {
            Name = Name,
            Version = Version,
            Repository = Repository,
            Commit = Commit
        };
    }
}
