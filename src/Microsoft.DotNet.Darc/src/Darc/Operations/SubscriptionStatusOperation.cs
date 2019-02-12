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
    class SubscriptionStatus : Operation
    {
        SubscriptionStatusCommandLineOptions _options;

        public SubscriptionStatus(SubscriptionStatusCommandLineOptions options)
            : base(options)
        {
            _options = options;
        }

        /// <summary>
        /// Implements the 'subscription-status' operation
        /// </summary>
        /// <param name="options"></param>
        public override async Task<int> ExecuteAsync()
        {
            if ((_options.Enable && _options.Disable) ||
                (!_options.Enable && !_options.Disable))
            {
                Logger.LogError("'enable' and 'disable' options should have different values...");
                return Constants.ErrorCode;
            }

            DarcSettings darcSettings = LocalSettings.GetDarcSettings(_options, Logger);
            // No need to set up a git type or PAT here.
            Remote remote = new Remote(darcSettings, Logger);

            // First, try to get the subscription. If it doesn't exist the call will throw and the exception will be
            // caught by `RunOperation`
            Subscription subscription = await remote.GetSubscriptionAsync(_options.Id);
            string statusMessage = "enable";

            if (!_options.Enable)
            {
                statusMessage = "disable";
            }

            if (subscription.Enabled == _options.Enable)
            {
                Console.WriteLine($"Status of subscription '{_options.Id}' is already {statusMessage}d, exiting...");

                return Constants.SuccessCode;
            }
           
            try
            {
                SubscriptionUpdate subscriptionToUpdate = new SubscriptionUpdate
                {
                    ChannelName = subscription.Channel.Name,
                    SourceRepository = subscription.SourceRepository,
                    Enabled = _options.Enable,
                    Policy = subscription.Policy
                };
                subscriptionToUpdate.Policy.Batchable = subscription.Policy.Batchable;
                subscriptionToUpdate.Policy.UpdateFrequency = subscription.Policy.UpdateFrequency;
                subscriptionToUpdate.Policy.MergePolicies = subscription.Policy.MergePolicies;

                var updatedSubscription = await remote.UpdateSubscriptionAsync(
                    subscription.Id.Value,
                    subscriptionToUpdate);

                Console.WriteLine($"Successfully {statusMessage}d subscription with id '{updatedSubscription.Id}'.");

                return Constants.SuccessCode;
            }
            catch (Exception e)
            {
                Logger.LogError(e, $"Failed to {statusMessage} subscription.");
                return Constants.ErrorCode;
            }
        }
    }
}
