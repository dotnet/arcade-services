// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Maestro.MergePolicyEvaluation;
using Maestro.DataProviders.ConfigurationIngestion.Model;

#nullable enable
namespace Maestro.DataProviders.ConfigurationIngestion.Validations;

internal class BranchMergePolicyValidator
{
    public static void ValidateBranchMergePolicies(
        IReadOnlyCollection<IngestedBranchMergePolicies> branchMergePolicies)
    {
        EntityValidator.ValidateEntityUniqueness(branchMergePolicies);

        foreach (var branchMergePolicy in branchMergePolicies)
        {
            ValidateBranchMergePolicies(branchMergePolicy);
        }
    }

    /// <summary>
    /// Validates a RepositoryBranch entity against business rules.
    /// </summary>
    /// <param name="branchMergePolicy">The RepositoryBranch to validate</param>
    /// <exception cref="ArgumentException">Thrown when validation fails</exception>
    public static void ValidateBranchMergePolicies(IngestedBranchMergePolicies branchMergePolicy)
    {
        ArgumentNullException.ThrowIfNull(branchMergePolicy);

        if (string.IsNullOrWhiteSpace(branchMergePolicy._values.Repository))
        {
            throw new IngestionEntityValidationException("Repository is required.", branchMergePolicy);
        }

        if (string.IsNullOrWhiteSpace(branchMergePolicy._values.Branch))
        {
            throw new IngestionEntityValidationException("Branch is required.", branchMergePolicy);
        }

        if (branchMergePolicy._values.MergePolicies == null)
        {
            throw new IngestionEntityValidationException("Merge policies cannot be null.", branchMergePolicy);
        }

        if (branchMergePolicy._values.Repository.Length > Data.Models.Repository.RepositoryNameLength)
        {
            throw new IngestionEntityValidationException($"Repository name cannot be longer than {Data.Models.Repository.RepositoryNameLength} characters.", branchMergePolicy);
        }

        if (branchMergePolicy._values.Branch.Length > Data.Models.Repository.BranchNameLength)
        {
            throw new IngestionEntityValidationException($"Branch name cannot be longer than {Data.Models.Repository.BranchNameLength} characters.", branchMergePolicy);
        }

        var mergePolicies = branchMergePolicy._values.MergePolicies.Select(mp => mp.Name);

        if (mergePolicies.Contains(MergePolicyConstants.StandardMergePolicyName)
            && mergePolicies.Any(SubscriptionValidator.StandardMergePolicies.Contains))
        {
            throw new IngestionEntityValidationException(
                "One or more of the following merge policies could not be added because it is already included "
                + $"in the standard merge policy: {string.Join(", ", SubscriptionValidator.StandardMergePolicies)}", branchMergePolicy);
        }
    }
}
