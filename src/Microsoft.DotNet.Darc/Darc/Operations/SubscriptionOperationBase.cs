// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Maestro.Common;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Models.Darc.Yaml;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Darc.Operations;

internal abstract class SubscriptionOperationBase : ConfigurationManagementOperation
{
    protected readonly IBarApiClient _barClient;
    protected readonly ILogger _logger;

    protected SubscriptionOperationBase(
        IBarApiClient barClient,
        ILogger logger,
        IConfigurationManagementCommandLineOptions options,
        IGitRepoFactory gitRepoFactory,
        IRemoteFactory remoteFactory,
        ILocalGitRepoFactory localGitRepoFactory) : base(options, gitRepoFactory, remoteFactory, logger, localGitRepoFactory)
    {
        _barClient = barClient;
        _logger = logger;
    }

    /// <summary>
    /// Validates that the codeflow subscription doesn't conflict with existing ones
    /// </summary>
    protected void ValidateCodeflowSubscriptionConflicts(
        IReadOnlyCollection<SubscriptionYamlData> existingSubscriptions,
        SubscriptionYamlData subscription)
    {
        if (subscription.SourceEnabled == "true")
        {
            return;
        }

        // Check for backflow conflicts (source directory not empty)
        if (!string.IsNullOrEmpty(subscription.SourceDirectory))
        {
            var conflictingBackflowSubscription = FindConflictingBackflowSubscription(existingSubscriptions, subscription);
            if (conflictingBackflowSubscription != null)
            {
                throw new DarcException($"A backflow subscription '{conflictingBackflowSubscription.Id}' already exists for the same target repository and branch. " +
                       "Only one backflow subscription is allowed per target repository and branch combination.");
            }
        }

        // Check for forward flow conflicts (target directory not empty)
        if (!string.IsNullOrEmpty(subscription.TargetDirectory))
        {
            var conflictingForwardFlowSubscription = FindConflictingForwardFlowSubscription(existingSubscriptions, subscription);
            if (conflictingForwardFlowSubscription != null)
            {
                throw new DarcException($"A forward flow subscription '{conflictingForwardFlowSubscription.Id}' already exists for the same VMR repository, branch, and target directory. " +
                       "Only one forward flow subscription is allowed per VMR repository, branch, and target directory combination.");
            }
        }
    }

    private static SubscriptionYamlData FindConflictingBackflowSubscription(IReadOnlyCollection<SubscriptionYamlData> existingSubscriptions, SubscriptionYamlData updatedOrNewSubscription) =>
        existingSubscriptions.FirstOrDefault(sub =>
            sub.SourceEnabled == "true"
                && !string.IsNullOrEmpty(sub.SourceDirectory) // Backflow subscription
                && sub.TargetRepository == updatedOrNewSubscription.TargetRepository
                && sub.TargetBranch == updatedOrNewSubscription.TargetBranch
                && sub.Id != updatedOrNewSubscription.Id);

    private static SubscriptionYamlData FindConflictingForwardFlowSubscription(IReadOnlyCollection<SubscriptionYamlData> existingSubscriptions, SubscriptionYamlData updatedOrNewSubscription) =>
        existingSubscriptions.FirstOrDefault(sub =>
            sub.SourceEnabled == "true"
                && !string.IsNullOrEmpty(sub.TargetDirectory) // Forward flow subscription
                && sub.TargetRepository == updatedOrNewSubscription.TargetRepository
                && sub.TargetBranch == updatedOrNewSubscription.TargetBranch
                && sub.TargetDirectory == updatedOrNewSubscription.TargetDirectory
                && sub.Id != updatedOrNewSubscription.Id);

    protected static string GetConfigurationFilePath(string repoUri)
    {
        try
        {
            var (repoName, owner) = GitRepoUrlUtils.GetRepoNameAndOwner(repoUri);
            return SubscriptionConfigurationFolderPath / $"{owner}-{repoName}.yml";
        }
        catch (Exception)
        {
            return SubscriptionConfigurationFolderPath / repoUri.Split('/', StringSplitOptions.RemoveEmptyEntries).Last() + ".yml";
        }
    }
    protected static string GetSubscriptionDescription(SubscriptionYamlData s) => $"({s.Id}) {s.SourceRepository} ({s.Channel}) ==> {s.TargetRepository} ({s.TargetBranch})";
}
