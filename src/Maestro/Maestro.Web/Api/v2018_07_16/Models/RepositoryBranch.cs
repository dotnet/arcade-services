// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace Maestro.Web.Api.v2018_07_16.Models
{
    public class RepositoryBranch : IValidatableObject
    {
        public RepositoryBranch(Data.Models.RepositoryBranch other)
        {
            Repository = other.RepositoryName;
            Branch = other.BranchName;
            MergePolicies = (other.PolicyObject?.MergePolicies ?? new List<Data.Models.MergePolicyDefinition>()).Select(p => new MergePolicy(p)).ToImmutableList();
        }

        public string Repository { get; set; }
        public string Branch { get; set; }
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
}
