using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.DotNet.Darc;
using Microsoft.DotNet.Maestro.Client.Models;
using Newtonsoft.Json.Linq;

namespace Maestro.ScenarioTests.ObjectHelpers
{
    public class SubscriptionBuilder
    {
        /// <summary>
        /// Creates a subscription object based on a standard set of test inputs
        /// </summary>
        public static Subscription BuildSubscription(string repo1Uri, string repo2Uri, string targetBranch, string channelName, string subscriptionId,
            UpdateFrequency updateFrequency, bool batchable, List<string> mergePolicyNames = null, List<string> ignoreChecks = null)
        {
            Subscription expectedSubscription = new Subscription(Guid.Parse(subscriptionId), true, repo1Uri, repo2Uri, targetBranch);
            expectedSubscription.Channel = new Channel(42, channelName, "test");
            expectedSubscription.Policy = new SubscriptionPolicy(batchable, updateFrequency);

            List<MergePolicy> mergePolicies = new List<MergePolicy>();

            if (mergePolicyNames == null)
            {
                expectedSubscription.Policy.MergePolicies = mergePolicies.ToImmutableList<MergePolicy>();
                return expectedSubscription;
            }

            if (mergePolicyNames.Contains(Constants.StandardMergePolicyName))
            {
                mergePolicies.Add(new MergePolicy
                {
                    Name = Constants.StandardMergePolicyName
                });
            }

            if (mergePolicyNames.Contains(Constants.NoExtraCommitsMergePolicyName))
            {
                mergePolicies.Add(
                    new MergePolicy
                    {
                        Name = Constants.NoExtraCommitsMergePolicyName
                    });
            }

            if (mergePolicyNames.Contains(Constants.AllCheckSuccessfulMergePolicyName) && ignoreChecks.Any())
            {
                mergePolicies.Add(
                    new MergePolicy
                    {
                        Name = Constants.AllCheckSuccessfulMergePolicyName,
                        Properties = ImmutableDictionary.Create<string, JToken>()
                            .Add(Constants.IgnoreChecksMergePolicyPropertyName, JToken.FromObject(ignoreChecks))
                    });
            }

            if (mergePolicyNames.Contains(Constants.NoRequestedChangesMergePolicyName))
            {
                mergePolicies.Add(
                    new MergePolicy
                    {
                        Name = Constants.NoRequestedChangesMergePolicyName,
                        Properties = ImmutableDictionary.Create<string, JToken>()
                    });
            }

            expectedSubscription.Policy.MergePolicies = mergePolicies.ToImmutableList<MergePolicy>();
            return expectedSubscription;
        }
    }
}
