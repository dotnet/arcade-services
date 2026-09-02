// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;

#nullable disable
namespace ProductConstructionService.Api.v2018_07_16.Models;

public class RepositoryBranch
{
    public RepositoryBranch(Maestro.Data.Models.RepositoryBranch other)
    {
        Repository = other.RepositoryName;
        Branch = other.BranchName;
        MergePrs = other.MergePrs;
        IgnoredChecks = [.. (other.IgnoredChecks ?? [])];
    }

    public string Repository { get; set; }
    public string Branch { get; set; }
    public bool MergePrs { get; set; }
    public IReadOnlyCollection<string> IgnoredChecks { get; set; }
}
