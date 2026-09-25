// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.ProductConstructionService.Client.Models;

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
        bool mergePrs = false,
        List<string> ignoreChecks = null,
        string failureNotificationTags = null)
    {
        var expectedSubscription = new Subscription(
            Guid.Parse(subscriptionId),
            mergePrs,
            true,
            false,
            false,
            repo1Uri,
            repo2Uri,
            targetBranch,
            ignoreChecks ?? [],
            pullRequestFailureNotificationTags: failureNotificationTags,
            sourceDirectory: null,
            targetDirectory: null,
            excludedAssets: [])
        {
            Channel = new Channel(42, channelName, "test"),
            Policy = new SubscriptionPolicy(batchable, updateFrequency)
        };

        return expectedSubscription;
    }
}
