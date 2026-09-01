// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;

#nullable disable
namespace ProductConstructionService.Api.v2018_07_16.Models;

public class RepositoryBranch : IValidatableObject
{
    public RepositoryBranch(Maestro.Data.Models.RepositoryBranch other)
    {
        Repository = other.RepositoryName;
        Branch = other.BranchName;
        MergePrs = other.MergePrs;
        IgnoredChecks = [.. other.IgnoredChecks];
        MergePolicies = (other.PolicyObject?.MergePolicies ?? []).Select(p => new MergePolicy(p)).ToImmutableList();
    }

    public string Repository { get; set; }
    public string Branch { get; set; }
    public bool MergePrs { get; set; }
    public IReadOnlyCollection<string> IgnoredChecks { get; set; }

    // TODO: Remove legacy merge policy support after the configuration migration.
    // https://github.com/dotnet/arcade-services/issues/6426
    public ImmutableList<MergePolicy> MergePolicies { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (MergePolicies != null &&
            MergePolicies.Select(policy => policy.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != MergePolicies.Count)
        {
            yield return new ValidationResult(
                "Repositories may not have duplicates of merge policies.",
                new[] { nameof(MergePolicies) });
        }
    }
}
