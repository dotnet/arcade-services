// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Maestro.DataProviders.ConfigurationIngestion.Model;
using Maestro.DataProviders.Exceptions;
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

        if (subscription.Values.Id == Guid.Empty)
        {
            throw new EntityIngestionValidationException("Subscription id is required.", subscription);
        }

        if (string.IsNullOrWhiteSpace(subscription.Values.Channel))
        {
            throw new EntityIngestionValidationException("Channel name is required.", subscription);
        }

        if (string.IsNullOrWhiteSpace(subscription.Values.SourceRepository))
        {
            throw new EntityIngestionValidationException("Source repository is required.", subscription);
        }

        if (string.IsNullOrWhiteSpace(subscription.Values.TargetRepository))
        {
            throw new EntityIngestionValidationException("Target repository is required.", subscription);
        }

        if (string.IsNullOrWhiteSpace(subscription.Values.TargetBranch))
        {
            throw new EntityIngestionValidationException("Target branch is required.", subscription);
        }

        if (subscription.Values.MergePolicies == null)
        {
            throw new EntityIngestionValidationException("Merge policies cannot be null.", subscription);
        }

        List<string> mergePolicies = [.. subscription.Values.MergePolicies.Select(mp => mp.Name)];

        if (!subscription.Values.SourceEnabled
            && mergePolicies.Contains(MergePolicyConstants.CodeflowMergePolicyName))
        {
            throw new EntityIngestionValidationException("Only source-enabled subscriptions may have the Codeflow merge policy.", subscription);
        }

        if (mergePolicies.Contains(MergePolicyConstants.StandardMergePolicyName)
            && mergePolicies.Any(StandardMergePolicies.Contains))
        {
            throw new EntityIngestionValidationException(
                "One or more of the following merge policies could not be added because it is already included "
                + $"in the policy `{MergePolicyConstants.StandardMergePolicyName}`: {string.Join(", ", StandardMergePolicies)}.", subscription);
        }

        if (subscription.Values.Batchable && subscription.Values.SourceEnabled)
        {
            throw new EntityIngestionValidationException("Batched codeflow subscriptions are not supported.", subscription);
        }

        if (subscription.Values.Batchable && mergePolicies.Count > 0)
        {
            throw new EntityIngestionValidationException(
                "Batchable subscriptions cannot be combined with merge policies. " +
                "Merge policies are specified at a repository+branch level.", subscription);
        }

        if (!string.IsNullOrEmpty(subscription.Values.SourceDirectory)
            && !string.IsNullOrEmpty(subscription.Values.TargetDirectory))
        {
            throw new EntityIngestionValidationException(
                "Only one of source or target directory can be specified for source-enabled subscriptions.", subscription);
        }

        if (subscription.Values.SourceEnabled
            && string.IsNullOrEmpty(subscription.Values.SourceDirectory)
            && string.IsNullOrEmpty(subscription.Values.TargetDirectory))
        {
            throw new EntityIngestionValidationException(
                "One of source or target directory is required for source-enabled subscriptions.", subscription);
        }
    }
}
