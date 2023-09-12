// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel.DataAnnotations;

namespace Maestro.ContainerApp.Api.Models;

public class BuildGraph
{
    public static BuildGraph Create(IEnumerable<Build> builds)
    {
        return new BuildGraph(builds.ToDictionary(b => b.Id, b => b));
    }

    public BuildGraph(IDictionary<int, Build> builds)
    {
        Builds = builds;
    }

    [Required]
    public IDictionary<int, Build> Builds { get; }
}
