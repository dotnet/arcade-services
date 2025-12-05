// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Maestro.Data.Models;
using Maestro.MergePolicyEvaluation;

#nullable enable
namespace Maestro.DataProviders.ConfigurationIngestor.Validations;

public class BranchMergePolicyValidator
{
    public static void ValidateBranchMergePolicies(
        IEnumerable<RepositoryBranch> branchMergePolicies)
    {
        EntityValidator.ValidateEntityUniqueness(branchMergePolicies);

        foreach (RepositoryBranch branchMergePolicy in branchMergePolicies)
        {
            ValidateBranchMergePolicies(branchMergePolicy);
        }
    }

    /// <summary>
    /// Validates a RepositoryBranch entity against business rules.
    /// </summary>
    /// <param name="branchMergePolicy">The RepositoryBranch to validate</param>
    /// <exception cref="ArgumentException">Thrown when validation fails</exception>
    public static void ValidateBranchMergePolicies(RepositoryBranch branchMergePolicy)
    {
        ArgumentNullException.ThrowIfNull(branchMergePolicy);

        ArgumentException.ThrowIfNullOrWhiteSpace(branchMergePolicy.RepositoryName);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchMergePolicy.BranchName);
        ArgumentNullException.ThrowIfNull(branchMergePolicy.PolicyObject);

        if (branchMergePolicy.RepositoryName.Length > Repository.RepositoryNameLength)
        {
            throw new ArgumentException($"Repository name cannot be longer than {Repository.RepositoryNameLength}.");
        }

        if (branchMergePolicy.BranchName.Length > Repository.BranchNameLength)
        {
            throw new ArgumentException($"Branch name cannot be longer than {Repository.BranchNameLength}.");
        }

        var mergePolicies = branchMergePolicy.PolicyObject.MergePolicies.Select(mp => mp.Name);

        if (mergePolicies.Contains(MergePolicyConstants.StandardMergePolicyName)
            && mergePolicies.Any(SubscriptionValidator.StandardMergePolicies.Contains))
        {
            throw new ArgumentException(
                "One or more of the following merge policies could not be added because it is already included "
                + $"in the standard merge policy: {string.Join(", ", SubscriptionValidator.StandardMergePolicies)}");
        }
    }
}
