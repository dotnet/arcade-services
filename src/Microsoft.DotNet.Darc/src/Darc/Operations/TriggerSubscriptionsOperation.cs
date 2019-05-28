// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.DotNet.Maestro.Client;

namespace Microsoft.DotNet.Darc.Operations
{
    internal class TriggerSubscriptionsOperation : Operation
    {
        TriggerSubscriptionsCommandLineOptions _options;
        public TriggerSubscriptionsOperation(TriggerSubscriptionsCommandLineOptions options)
            : base(options)
        {
            _options = options;
        }

        /// <summary>
        /// Triggers subscriptions
        /// </summary>
        /// <returns></returns>
        public override async Task<int> ExecuteAsync()
        {
            try
            {
                IRemote remote = RemoteFactory.GetBarOnlyRemote(_options, Logger);

                bool noConfirm = _options.NoConfirmation;
                List<Subscription> subscriptionsToTrigger = new List<Subscription>();

                if (!string.IsNullOrEmpty(_options.Id))
                {
                    // Look up subscription so we can print it later.
                    try
                    {
                        Subscription subscription = await remote.GetSubscriptionAsync(_options.Id);
                        subscriptionsToTrigger.Add(subscription);
                    }
                    catch (RestApiException e) when (e.Response.StatusCode == HttpStatusCode.NotFound)
                    {
                        Console.WriteLine($"Subscription with id '{_options.Id}' was not found.");
                        return Constants.ErrorCode;
                    }
                }
                else
                {
                    if (string.IsNullOrEmpty(_options.TargetRepository) &&
                        string.IsNullOrEmpty(_options.TargetBranch) &&
                        string.IsNullOrEmpty(_options.SourceRepository) &&
                        string.IsNullOrEmpty(_options.Channel))
                    {
                        Console.WriteLine($"Please specify one or more filters to select which subscriptions should be triggered (see help).");
                        return Constants.ErrorCode;
                    }

                    IEnumerable<Subscription> subscriptions = (await remote.GetSubscriptionsAsync()).Where(subscription =>
                    {
                        return _options.SubcriptionFilter(subscription);
                    });

                    if (subscriptions.Count() == 0)
                    {
                        Console.WriteLine("No subscriptions found matching the specified criteria.");
                        return Constants.ErrorCode;
                    }

                    subscriptionsToTrigger.AddRange(subscriptions);
                }

                if (!noConfirm)
                {
                    // Print out the list of subscriptions about to be triggered.
                    Console.WriteLine($"Will trigger the following {subscriptionsToTrigger.Count} subscriptions...");
                    foreach (var subscription in subscriptionsToTrigger)
                    {
                        Console.WriteLine($"  {UxHelpers.GetSubscriptionDescription(subscription)}");
                    }

                    char keyChar;
                    do
                    {
                        Console.Write("Continue? (y/n) ");
                        ConsoleKeyInfo keyInfo = Console.ReadKey();
                        keyChar = char.ToUpperInvariant(keyInfo.KeyChar);
                        Console.WriteLine();
                    }
                    while (keyChar != 'Y' && keyChar != 'N');

                    if (keyChar == 'N')
                    {
                        Console.WriteLine($"No subscriptions triggered, exiting.");
                        return Constants.ErrorCode;
                    }
                }

                Console.Write($"Triggering {subscriptionsToTrigger.Count} subscriptions...{(noConfirm ? Environment.NewLine : "")}");
                foreach (var subscription in subscriptionsToTrigger)
                {
                    // If noConfirm was passed, print out the subscriptions as we go
                    if (noConfirm)
                    {
                        Console.WriteLine($"  {UxHelpers.GetSubscriptionDescription(subscription)}");
                    }
                    await remote.TriggerSubscriptionAsync(subscription.Id.ToString());
                }
                Console.WriteLine($"done");

                return Constants.SuccessCode;
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Unexpected error while triggering subscriptions.");
                return Constants.ErrorCode;
            }
        }
    }
}
