// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using JetBrains.Annotations;
using Maestro.Data.Models;

namespace Maestro.Web.Api.v2018_07_16.Models
{
    public class SubscriptionPolicy : IValidatableObject
    {
        public SubscriptionPolicy()
        {
        }

        public SubscriptionPolicy([NotNull] Data.Models.SubscriptionPolicy other)
        {
            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }

            Batchable = other.Batchable;
            UpdateFrequency = (UpdateFrequency) (int) other.UpdateFrequency;
            MergePolicies = other.MergePolicies != null
                ? other.MergePolicies.Select(p => new MergePolicy(p)).ToImmutableList()
                : ImmutableList<MergePolicy>.Empty;
        }

        public bool Batchable { get; set; } = false;

        [Required]
        public UpdateFrequency UpdateFrequency { get; set; }

        public IImmutableList<MergePolicy> MergePolicies { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            // Disallow two things:
            // - Merge policies set on batchable subscriptions. They should be set as a repository policy
            // - More than one instance of a single policy.
            if (MergePolicies != null && MergePolicies.Count != 0)
            {
                if (Batchable)
                {
                    yield return new ValidationResult(
                        "Batchable Subscriptions cannot have any merge policies.",
                        new[] { nameof(MergePolicies), nameof(Batchable) });
                }
                else if (MergePolicies.Select(policy => policy.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != MergePolicies.Count)
                {
                    yield return new ValidationResult(
                        "Subscriptions may not have duplicates of merge policies.",
                        new[] { nameof(MergePolicies) });
                }
            }
        }

        public Data.Models.SubscriptionPolicy ToDb()
        {
            return new Data.Models.SubscriptionPolicy
            {
                Batchable = Batchable,
                MergePolicies = MergePolicies?.Select(p => p.ToDb()).ToList() ?? new List<MergePolicyDefinition>(),
                UpdateFrequency = (Data.Models.UpdateFrequency) (int) UpdateFrequency
            };
        }
    }
}
