﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable
using System.Collections.Generic;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.ProductConstructionService.Client.Models;

namespace Microsoft.DotNet.DarcLib.Models;

public record VersionDetails(
    IReadOnlyCollection<DependencyDetail> Dependencies,
    SourceDependency? Source);

public record SourceDependency(string Uri, string Sha, int? BarId)
{
    public SourceDependency(Build build)
        : this(build.GetRepository(), build.Commit, build.Id)
    {
    }
}

