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
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.Maestro.Client;
using ApiError = Microsoft.DotNet.Maestro.Client.Models.ApiError;

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
            IRemote remote = RemoteFactory.GetBarOnlyRemote(_options, Logger);

            // First, try to get the subscription. If it doesn't exist the call will throw and the exception will be
            // caught by `RunOperation`
            Subscription subscription = await remote.GetSubscriptionAsync(_options.Id);

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

            string channel = updateSubscriptionPopUp.Channel;
            string sourceRepository = updateSubscriptionPopUp.SourceRepository;
            string updateFrequency = updateSubscriptionPopUp.UpdateFrequency;
            bool batchable = updateSubscriptionPopUp.Batchable;
            bool enabled = updateSubscriptionPopUp.Enabled;
            List<MergePolicy> mergePolicies = updateSubscriptionPopUp.MergePolicies;

            try
            {
                SubscriptionUpdate subscriptionToUpdate = new SubscriptionUpdate
                {
                    ChannelName = channel ?? subscription.Channel.Name,
                    SourceRepository = sourceRepository ?? subscription.SourceRepository,
                    Enabled = enabled,
                    Policy = subscription.Policy,
                };
                subscriptionToUpdate.Policy.Batchable = batchable;
                subscriptionToUpdate.Policy.UpdateFrequency = Enum.Parse<UpdateFrequency>(updateFrequency);
                subscriptionToUpdate.Policy.MergePolicies = mergePolicies.ToImmutableList();

                var updatedSubscription = await remote.UpdateSubscriptionAsync(
                    _options.Id,
                    subscriptionToUpdate);

                Console.WriteLine($"Successfully updated subscription with id '{updatedSubscription.Id}'.");

                return Constants.SuccessCode;
            }
            catch (RestApiException e) when (e.Response.StatusCode == System.Net.HttpStatusCode.BadRequest)
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
