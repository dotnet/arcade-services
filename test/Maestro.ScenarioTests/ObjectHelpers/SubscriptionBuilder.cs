// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet.Maestro.Client.Models;
using Newtonsoft.Json.Linq;

namespace Maestro.ScenarioTests.ObjectHelpers;

public class SubscriptionBuilder
{
    /// <summary>
    /// Creates a subscription object based on a standard set of test inputs
    /// </summary>
    public static Subscription BuildSubscription(
        string repo1Uri,
        string repo2Uri,
        string targetBranch,
        string channelName,
        string subscriptionId,
        UpdateFrequency updateFrequency,
        bool batchable,
        List<string> mergePolicyNames = null,
        List<string> ignoreChecks = null,
        string failureNotificationTags = null)
    {
        var expectedSubscription = new Subscription(
            Guid.Parse(subscriptionId),
            true,
            false,
            repo1Uri,
            repo2Uri,
            targetBranch,
            failureNotificationTags,
            excludedAssets: ImmutableList<string>.Empty,
            sourceDirectory: null)
        {
            Channel = new Channel(42, channelName, "test"),
            Policy = new SubscriptionPolicy(batchable, updateFrequency)
        };

        List<MergePolicy> mergePolicies = [];

        if (mergePolicyNames == null)
        {
            expectedSubscription.Policy.MergePolicies = mergePolicies.ToImmutableList();
            return expectedSubscription;
        }

        if (mergePolicyNames.Contains(MergePolicyConstants.StandardMergePolicyName))
        {
            mergePolicies.Add(new MergePolicy
            {
                Name = MergePolicyConstants.StandardMergePolicyName
            });
        }

        if (mergePolicyNames.Contains(MergePolicyConstants.AllCheckSuccessfulMergePolicyName) && ignoreChecks.Any())
        {
            mergePolicies.Add(
                new MergePolicy
                {
                    Name = MergePolicyConstants.AllCheckSuccessfulMergePolicyName,
                    Properties = ImmutableDictionary.Create<string, JToken>()
                        .Add(MergePolicyConstants.IgnoreChecksMergePolicyPropertyName, JToken.FromObject(ignoreChecks))
                });
        }

        if (mergePolicyNames.Contains(MergePolicyConstants.NoRequestedChangesMergePolicyName))
        {
            mergePolicies.Add(
                new MergePolicy
                {
                    Name = MergePolicyConstants.NoRequestedChangesMergePolicyName,
                    Properties = ImmutableDictionary.Create<string, JToken>()
                });
        }

        if (mergePolicyNames.Contains(MergePolicyConstants.ValidateCoherencyMergePolicyName))
        {
            mergePolicies.Add(
                new MergePolicy {
                    Name = MergePolicyConstants.ValidateCoherencyMergePolicyName,
                    Properties = ImmutableDictionary.Create<string, JToken>()
                });
        }

        expectedSubscription.Policy.MergePolicies = mergePolicies.ToImmutableList();
        return expectedSubscription;
    }
}
