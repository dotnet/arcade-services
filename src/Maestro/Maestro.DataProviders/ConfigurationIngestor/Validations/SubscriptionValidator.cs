// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Maestro.MergePolicyEvaluation;
using Maestro.Data.Models;

namespace Maestro.DataProviders.ConfigurationIngestor.Validations;

public static class SubscriptionValidator
{
    public static HashSet<string> StandardMergePolicies = [
            MergePolicyConstants.AllCheckSuccessfulMergePolicyName,
            MergePolicyConstants.NoRequestedChangesMergePolicyName,
            MergePolicyConstants.DontAutomergeDowngradesPolicyName,
            MergePolicyConstants.ValidateCoherencyMergePolicyName,
            MergePolicyConstants.VersionDetailsPropsMergePolicyName,
            MergePolicyConstants.CodeflowMergePolicyName,
        ];

    /// <summary>
    /// Validates a subscription entity against business rules.
    /// </summary>
    /// <param name="subscription">The subscription to validate</param>
    /// <exception cref="ArgumentException">Thrown when validation fails</exception>
    public static void ValidateSubscription(
        Subscription subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentException.ThrowIfNullOrWhiteSpace(subscription.Channel.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(subscription.SourceRepository);
        ArgumentException.ThrowIfNullOrWhiteSpace(subscription.TargetRepository);
        ArgumentException.ThrowIfNullOrWhiteSpace(subscription.TargetBranch);
        ArgumentNullException.ThrowIfNull(subscription.PolicyObject);
        ArgumentNullException.ThrowIfNull(subscription.PolicyObject.MergePolicies);

        List<string> mergePolicies = [.. subscription.PolicyObject.MergePolicies.Select(mp => mp.Name)];

        if (!subscription.SourceEnabled
            && mergePolicies.Contains(MergePolicyConstants.CodeflowMergePolicyName))
        {
            throw new ArgumentException("Only source-enabled subscriptions may have the Codeflow merge policy.");
        }

        if (mergePolicies.Contains(MergePolicyConstants.StandardMergePolicyName)
            && mergePolicies.Any(StandardMergePolicies.Contains))
        {
            throw new ArgumentException(
                "One or more of the following merge policies could not be added because it is already included "
                + $"in the policy `{MergePolicyConstants.StandardMergePolicyName}`: {string.Join(", ", StandardMergePolicies)}.");
        }

        if (subscription.PolicyObject.Batchable && subscription.SourceEnabled)
        {
            throw new ArgumentException("Batched codeflow subscriptions are not supported.");
        }

        if (subscription.PolicyObject.Batchable && mergePolicies.Count > 0)
        {
            throw new ArgumentException(
                "Batchable subscriptions cannot be combined with merge policies. " +
                "Merge policies are specified at a repository+branch level.");
        }

        if (!string.IsNullOrEmpty(subscription.SourceDirectory)
            && !string.IsNullOrEmpty(subscription.TargetDirectory))
        {
            throw new ArgumentException(
                "Only one of source or target directory can be specified for source-enabled subscriptions.");
        }

        if (subscription.SourceEnabled
            && string.IsNullOrEmpty(subscription.SourceDirectory)
            && string.IsNullOrEmpty(subscription.TargetDirectory))
        {
            throw new ArgumentException(
                "One of source or target directory is required for source-enabled subscriptions.");
        }
    }
}
