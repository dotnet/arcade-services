// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Newtonsoft.Json.Linq;

namespace ProductConstructionService.ScenarioTests.Helpers;

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
            pullRequestFailureNotificationTags: failureNotificationTags,
            sourceDirectory: null,
            targetDirectory: null,
            excludedAssets: [])
        {
            Channel = new Channel(42, channelName, "test"),
            Policy = new SubscriptionPolicy(batchable, updateFrequency)
        };

        List<MergePolicy> mergePolicies = [];

        if (mergePolicyNames == null)
        {
            expectedSubscription.Policy.MergePolicies = mergePolicies;
            return expectedSubscription;
        }

        if (mergePolicyNames.Contains(MergePolicyConstants.StandardMergePolicyName))
        {
            mergePolicies.Add(new MergePolicy
            {
                Name = MergePolicyConstants.StandardMergePolicyName
            });
        }

        if (mergePolicyNames.Contains(MergePolicyConstants.AllCheckSuccessfulMergePolicyName) && ignoreChecks.Count != 0)
        {
            mergePolicies.Add(
                new MergePolicy
                {
                    Name = MergePolicyConstants.AllCheckSuccessfulMergePolicyName,
                    Properties = new() { [MergePolicyConstants.IgnoreChecksMergePolicyPropertyName] = JToken.FromObject(ignoreChecks) }
                });
        }

        if (mergePolicyNames.Contains(MergePolicyConstants.NoRequestedChangesMergePolicyName))
        {
            mergePolicies.Add(
                new MergePolicy
                {
                    Name = MergePolicyConstants.NoRequestedChangesMergePolicyName,
                    Properties = []
                });
        }

        if (mergePolicyNames.Contains(MergePolicyConstants.ValidateCoherencyMergePolicyName))
        {
            mergePolicies.Add(
                new MergePolicy
                {
                    Name = MergePolicyConstants.ValidateCoherencyMergePolicyName,
                    Properties = []
                });
        }

        if (mergePolicyNames.Contains(MergePolicyConstants.CodeflowMergePolicyName))
        {
            mergePolicies.Add(
                new MergePolicy
                {
                    Name = MergePolicyConstants.CodeflowMergePolicyName,
                    Properties = []
                });
        }

        expectedSubscription.Policy.MergePolicies = mergePolicies;
        return expectedSubscription;
    }
}
