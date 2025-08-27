// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Darc.Operations;

internal abstract class SubscriptionOperationBase : Operation
{
    protected readonly IBarApiClient _barClient;
    protected readonly ILogger _logger;

    protected SubscriptionOperationBase(IBarApiClient barClient, ILogger logger)
    {
        _barClient = barClient;
        _logger = logger;
    }

    /// <summary>
    /// Validates that the codeflow subscription doesn't conflict with existing ones
    /// </summary>
    protected async Task ValidateCodeflowSubscriptionConflicts(
        string sourceRepository,
        string targetRepository,
        string targetBranch,
        string sourceDirectory,
        string targetDirectory,
        Guid? existingSubscriptionId)
    {
        // Check for backflow conflicts (source directory not empty)
        if (!string.IsNullOrEmpty(sourceDirectory))
        {
            var backflowSubscriptions = await _barClient.GetCodeflowSubscriptionsAsync(
                targetRepo: targetRepository,
                sourceEnabled: true);

            var conflictingBackflowSubscription = backflowSubscriptions.FirstOrDefault(sub =>
                !string.IsNullOrEmpty(sub.SourceDirectory) &&
                sub.TargetRepository == targetRepository &&
                sub.TargetBranch == targetBranch &&
                sub.Id != existingSubscriptionId);

            if (conflictingBackflowSubscription != null)
            {
                _logger.LogError($"A backflow subscription '{conflictingBackflowSubscription.Id}' already exists for the same target repository and branch. " +
                               "Only one backflow subscription is allowed per target repository and branch combination.");
                throw new ArgumentException("Codeflow subscription conflict detected.");
            }
        }

        // Check for forward flow conflicts (target directory not empty)
        if (!string.IsNullOrEmpty(targetDirectory))
        {
            var forwardFlowSubscriptions = await _barClient.GetCodeflowSubscriptionsAsync(
                targetRepo: targetRepository,
                sourceEnabled: true,
                targetDirectory: targetDirectory);

            var conflictingForwardFlowSubscription = forwardFlowSubscriptions.FirstOrDefault(sub =>
                !string.IsNullOrEmpty(sub.TargetDirectory) &&
                sub.TargetRepository == targetRepository &&
                sub.TargetBranch == targetBranch &&
                sub.TargetDirectory == targetDirectory &&
                sub.Id != existingSubscriptionId);

            if (conflictingForwardFlowSubscription != null)
            {
                _logger.LogError($"A forward flow subscription '{conflictingForwardFlowSubscription.Id}' already exists for the same VMR repository, branch, and target directory. " +
                               "Only one forward flow subscription is allowed per VMR repository, branch, and target directory combination.");
                throw new ArgumentException("Codeflow subscription conflict detected.");
            }
        }
    }
}