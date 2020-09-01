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
using Microsoft.AspNetCore.Http.Features;

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
                    catch (RestApiException e) when (e.Response.Status == (int) HttpStatusCode.NotFound)
                    {
                        Console.WriteLine($"Subscription with id '{_options.Id}' was not found.");
                        return Constants.ErrorCode;
                    }
                }
                else
                {
                    if (!_options.HasAnyFilters())
                    {
                        Console.WriteLine($"Please specify one or more filters to select which subscriptions should be triggered (see help).");
                        return Constants.ErrorCode;
                    }

                    IEnumerable<Subscription> subscriptions = await _options.FilterSubscriptions(remote);

                    if (!subscriptions.Any())
                    {
                        Console.WriteLine("No subscriptions found matching the specified criteria.");
                        return Constants.ErrorCode;
                    }

                    subscriptionsToTrigger.AddRange(subscriptions);
                }

                if (_options.Build != 0)
                {
                    var specificBuild = await remote.GetBuildAsync(_options.Build);
                    if (specificBuild == null)
                    {
                        Console.WriteLine($"No build found in the BAR with id '{_options.Build}'");
                        return Constants.ErrorCode;
                    }

                    // If the user specified repo and a build number, error out if anything doesn't match.
                    if (!_options.SubscriptionParameterMatches(_options.SourceRepository, specificBuild.GitHubRepository))
                    {
                        Console.WriteLine($"Build #{_options.Build} was made with repo {specificBuild.GitHubRepository} and does not match provided value ({_options.SourceRepository})");
                        return Constants.ErrorCode;
                    }

                    Console.WriteLine($"Subscription updates will use Build # {_options.Build} instead of latest available");
                }

                // Filter away subscriptions that are disabled
                List<Subscription> disabledSubscriptions = subscriptionsToTrigger.Where(s => !s.Enabled).ToList();
                subscriptionsToTrigger = subscriptionsToTrigger.Where(s => s.Enabled).ToList();

                if (disabledSubscriptions.Any())
                {
                    Console.WriteLine($"The following {disabledSubscriptions.Count} subscription(s) are disabled and will not be triggered");
                    foreach (var subscription in disabledSubscriptions)
                    {
                        Console.WriteLine($"  {UxHelpers.GetSubscriptionDescription(subscription)}");
                    }
                }

                if (!subscriptionsToTrigger.Any())
                {
                    Console.WriteLine("No enabled subscriptions found matching the specified criteria.");
                    return Constants.ErrorCode;
                }

                if (!noConfirm)
                {
                    // Print out the list of subscriptions about to be triggered.
                    Console.WriteLine($"Will trigger the following {subscriptionsToTrigger.Count} subscriptions...");
                    foreach (var subscription in subscriptionsToTrigger)
                    {
                        Console.WriteLine($"  {UxHelpers.GetSubscriptionDescription(subscription)}");
                    }

                    if (!UxHelpers.PromptForYesNo("Continue?"))
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
                    if (_options.Build > 0)
                    {
                        await remote.TriggerSubscriptionAsync(subscription.Id.ToString(), _options.Build);
                    }
                    else
                    {
                        await remote.TriggerSubscriptionAsync(subscription.Id.ToString());
                    }
                }
                Console.WriteLine("done");

                return Constants.SuccessCode;
            }
            catch (AuthenticationException e)
            {
                Console.WriteLine(e.Message);
                return Constants.ErrorCode;
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Unexpected error while triggering subscriptions.");
                return Constants.ErrorCode;
            }
        }
    }
}
