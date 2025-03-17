// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.ProductConstructionService.Client.Models;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Models;

public record VersionDetails(
    IReadOnlyCollection<DependencyDetail> Dependencies,
    SourceDependency? Source);

public record SourceDependency(string Uri, string Mapping, string Sha, int? BarId)
{
    public SourceDependency(Build build, string mapping)
        : this(build.GetRepository(), mapping, build.Commit, build.Id)
    {
    }
}

