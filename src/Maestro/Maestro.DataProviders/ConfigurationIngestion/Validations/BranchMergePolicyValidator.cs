// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Maestro.MergePolicyEvaluation;
using Maestro.DataProviders.ConfigurationIngestion.Helpers;

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

        ArgumentException.ThrowIfNullOrWhiteSpace(branchMergePolicy.Values.Repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchMergePolicy.Values.Branch);
        ArgumentNullException.ThrowIfNull(branchMergePolicy.Values.MergePolicies);

        if (branchMergePolicy.Values.Repository.Length > Data.Models.Repository.RepositoryNameLength)
        {
            throw new ArgumentException($"Repository name cannot be longer than {Data.Models.Repository.RepositoryNameLength}.");
        }

        if (branchMergePolicy.Values.Branch.Length > Data.Models.Repository.BranchNameLength)
        {
            throw new ArgumentException($"Branch name cannot be longer than {Data.Models.Repository.BranchNameLength}.");
        }

        var mergePolicies = branchMergePolicy.Values.MergePolicies.Select(mp => mp.Name);

        if (mergePolicies.Contains(MergePolicyConstants.StandardMergePolicyName)
            && mergePolicies.Any(SubscriptionValidator.StandardMergePolicies.Contains))
        {
            throw new ArgumentException(
                "One or more of the following merge policies could not be added because it is already included "
                + $"in the standard merge policy: {string.Join(", ", SubscriptionValidator.StandardMergePolicies)}");
        }
    }
}
