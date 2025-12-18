// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Maestro.DataProviders.ConfigurationIngestion.Model;
using Maestro.MergePolicyEvaluation;

#nullable enable
namespace Maestro.DataProviders.ConfigurationIngestion.Validations;

internal static class SubscriptionValidator
{
    internal static HashSet<string> StandardMergePolicies = [
            MergePolicyConstants.AllCheckSuccessfulMergePolicyName,
            MergePolicyConstants.NoRequestedChangesMergePolicyName,
            MergePolicyConstants.DontAutomergeDowngradesPolicyName,
            MergePolicyConstants.ValidateCoherencyMergePolicyName,
            MergePolicyConstants.VersionDetailsPropsMergePolicyName,
            MergePolicyConstants.CodeflowMergePolicyName,
        ];

    /// <summary>
    /// Validates a collection of Subscription entities against business rules.
    /// </summary>
    /// <param name="subscriptions">The subscription collection to validate</param>
    /// <exception cref="ArgumentException">Thrown when validation fails</exception>
    internal static void ValidateSubscriptions(
        IEnumerable<IngestedSubscription> subscriptions)
    {
        EntityValidator.ValidateEntityUniqueness(subscriptions);

        foreach (var subscription in subscriptions)
        {
            ValidateSubscription(subscription);
        }
    }

    internal static void ValidateSubscription(
        IngestedSubscription subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        ArgumentException.ThrowIfNullOrWhiteSpace(subscription.Values.Channel);
        ArgumentException.ThrowIfNullOrWhiteSpace(subscription.Values.SourceRepository);
        ArgumentException.ThrowIfNullOrWhiteSpace(subscription.Values.TargetRepository);
        ArgumentException.ThrowIfNullOrWhiteSpace(subscription.Values.TargetBranch);
        ArgumentNullException.ThrowIfNull(subscription.Values.MergePolicies);

        List<string> mergePolicies = [.. subscription.Values.MergePolicies.Select(mp => mp.Name)];

        if (!subscription.Values.SourceEnabled
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

        if (subscription.Values.Batchable && subscription.Values.SourceEnabled)
        {
            throw new ArgumentException("Batched codeflow subscriptions are not supported.");
        }

        if (subscription.Values.Batchable && mergePolicies.Count > 0)
        {
            throw new ArgumentException(
                "Batchable subscriptions cannot be combined with merge policies. " +
                "Merge policies are specified at a repository+branch level.");
        }

        if (!string.IsNullOrEmpty(subscription.Values.SourceDirectory)
            && !string.IsNullOrEmpty(subscription.Values.TargetDirectory))
        {
            throw new ArgumentException(
                "Only one of source or target directory can be specified for source-enabled subscriptions.");
        }

        if (subscription.Values.SourceEnabled
            && string.IsNullOrEmpty(subscription.Values.SourceDirectory)
            && string.IsNullOrEmpty(subscription.Values.TargetDirectory))
        {
            throw new ArgumentException(
                "One of source or target directory is required for source-enabled subscriptions.");
        }
    }
}
