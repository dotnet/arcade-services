// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Models.PopUps;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Darc.Operations
{
    class UpdateSubscriptionOperation : Operation
    {
        UpdateSubscriptionCommandLineOptions _options;

        public UpdateSubscriptionOperation(UpdateSubscriptionCommandLineOptions options)
            : base(options)
        {
            _options = options;
        }

        /// <summary>
        /// Implements the 'update-subscription' operation
        /// </summary>
        /// <param name="options"></param>
        public override async Task<int> ExecuteAsync()
        {
            DarcSettings darcSettings = LocalSettings.GetDarcSettings(_options, Logger);
            // No need to set up a git type or PAT here.
            Remote remote = new Remote(darcSettings, Logger);

            if (_options.IgnoreChecks.Count() > 0 && !_options.AllChecksSuccessfulMergePolicy)
            {
                Logger.LogError($"--ignore-checks must be combined with --all-checks-passed");
                return Constants.ErrorCode;
            }
            // Parse the merge policies
            List<MergePolicy> mergePolicies = new List<MergePolicy>();

            if (_options.NoExtraCommitsMergePolicy)
            {
                mergePolicies.Add(new MergePolicy("NoExtraCommits", null));
            }

            if (_options.AllChecksSuccessfulMergePolicy)
            {
                mergePolicies.Add(new MergePolicy("AllChecksSuccessful", new Dictionary<string, object>
                {
                    { "ignoreChecks", _options.IgnoreChecks }
                }));
            }

            if (_options.RequireChecksMergePolicy.Count() > 0)
            {
                mergePolicies.Add(new MergePolicy("RequireChecks", new Dictionary<string, object>
                {
                    { "checks", _options.RequireChecksMergePolicy }
                }));
            }

            string channel = _options.Channel;
            string sourceRepository = _options.SourceRepository;
            string updateFrequency = _options.UpdateFrequency;
            bool batchable = _options.Batchable;
            bool enabled = _options.Enabled;

            // First, try to get the subscription. If it doesn't exist the call will throw and the exception will be
            // caught by `RunOperation`
            Subscription subscription = await remote.GetSubscriptionAsync(_options.Id);

            if (!_options.Quiet)
            {
                var suggestedRepos = remote.GetSubscriptionsAsync();
                var suggestedChannels = remote.GetChannelsAsync();

                UpdateSubscriptionPopUp updateSubscriptionPopUp = new UpdateSubscriptionPopUp(
                    "update-subscription/update-subscription-todo",
                    Logger,
                    subscription,
                    (await suggestedChannels).Select(suggestedChannel => suggestedChannel.Name),
                    (await suggestedRepos).SelectMany(subs => new List<string> { subscription.SourceRepository, subscription.TargetRepository }).ToHashSet(),
                    Constants.AvailableFrequencies,
                    Constants.AvailableMergePolicyYamlHelp);

                UxManager uxManager = new UxManager(Logger);

                int exitCode = uxManager.PopUp(updateSubscriptionPopUp);

                if (exitCode != Constants.SuccessCode)
                {
                    return exitCode;
                }

                channel = updateSubscriptionPopUp.Channel;
                sourceRepository = updateSubscriptionPopUp.SourceRepository;
                updateFrequency = updateSubscriptionPopUp.UpdateFrequency;
                batchable = updateSubscriptionPopUp.Batchable;
                enabled = updateSubscriptionPopUp.Enabled;
                mergePolicies = updateSubscriptionPopUp.MergePolicies;
            }

            try
            {
                SubscriptionUpdate subscriptionToUpdate = new SubscriptionUpdate
                {
                    ChannelName = channel ?? subscription.Channel.Name,
                    SourceRepository = sourceRepository ?? subscription.SourceRepository,
                    Enabled = enabled,
                    Policy = subscription.Policy
                };
                subscriptionToUpdate.Policy.Batchable = batchable;
                subscriptionToUpdate.Policy.UpdateFrequency = subscription.Policy.UpdateFrequency;
                subscriptionToUpdate.Policy.MergePolicies = mergePolicies.Any() ? mergePolicies : subscriptionToUpdate.Policy.MergePolicies;

                var updatedSubscription = await remote.UpdateSubscriptionAsync(
                    subscription.Id.Value,
                    subscriptionToUpdate);

                Console.WriteLine($"Successfully updated subscription with id '{updatedSubscription.Id}'.");

                return Constants.SuccessCode;
            }
            catch (ApiErrorException e) when (e.Response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                // Could have been some kind of validation error (e.g. channel doesn't exist)
                Logger.LogError($"Failed to update subscription: {e.Response.Content}");
                return Constants.ErrorCode;
            }
            catch (Exception e)
            {
                Logger.LogError(e, $"Failed to update subscription.");
                return Constants.ErrorCode;
            }
        }
    }
}
