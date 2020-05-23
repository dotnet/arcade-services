using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
        public static Subscription BuildSubscription(string repo1Uri, string repo2Uri, string targetBranch, string channelName, string batchSubscriptionId,
            UpdateFrequency updateFrequency, bool batchable, bool standardPolicy, bool noExtraCommits, bool allChecksSuccessful, bool noRequestedChanges, IEnumerable<string> ignoreChecks)
        {
            Subscription expectedBatchedSubscription = new Subscription(Guid.Parse(batchSubscriptionId), true, repo1Uri, repo2Uri, targetBranch);
            expectedBatchedSubscription.Channel = new Channel(42, channelName, "test");
            expectedBatchedSubscription.Policy = new SubscriptionPolicy(batchable, updateFrequency);


            List<MergePolicy> mergePolicies = new List<MergePolicy>();

            if (standardPolicy)
            {
                mergePolicies.Add(new MergePolicy
                {
                    Name = Constants.StandardMergePolicyName
                });
            }

            if (noExtraCommits)
            {
                mergePolicies.Add(
                    new MergePolicy
                    {
                        Name = Constants.NoExtraCommitsMergePolicyName
                    });
            }

            if (allChecksSuccessful)
            {
                mergePolicies.Add(
                    new MergePolicy
                    {
                        Name = Constants.AllCheckSuccessfulMergePolicyName,
                        Properties = ImmutableDictionary.Create<string, JToken>()
                            .Add(Constants.IgnoreChecksMergePolicyPropertyName, JToken.FromObject(ignoreChecks))
                    });
            }

            if (noRequestedChanges)
            {
                mergePolicies.Add(
                    new MergePolicy
                    {
                        Name = Constants.NoRequestedChangesMergePolicyName,
                        Properties = ImmutableDictionary.Create<string, JToken>()
                    });
            }

            expectedBatchedSubscription.Policy.MergePolicies = mergePolicies.ToImmutableList<MergePolicy>(); ;

            return expectedBatchedSubscription;
        }
    }
}
