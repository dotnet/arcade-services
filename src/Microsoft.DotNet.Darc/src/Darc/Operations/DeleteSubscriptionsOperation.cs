// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.DotNet.Maestro.Client;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.DotNet.Darc.Operations
{
    internal class DeleteSubscriptionsOperation : Operation
    {
        DeleteSubscriptionsCommandLineOptions _options;
        public DeleteSubscriptionsOperation(DeleteSubscriptionsCommandLineOptions options)
            : base(options)
        {
            _options = options;
        }

        public override async Task<int> ExecuteAsync()
        {
            try
            {
                IRemote remote = RemoteFactory.GetBarOnlyRemote(_options, Logger);

                bool noConfirm = _options.NoConfirmation;
                List<Subscription> subscriptionsToDelete = new List<Subscription>();

                if (!string.IsNullOrEmpty(_options.Id))
                {
                    // Look up subscription so we can print it later.
                    try
                    {
                        Subscription subscription = await remote.GetSubscriptionAsync(_options.Id);
                        subscriptionsToDelete.Add(subscription);
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
                        Console.WriteLine($"Please specify one or more filters to select which subscriptions should be deleted (see help).");
                        return Constants.ErrorCode;
                    }

                    IEnumerable<Subscription> subscriptions = await _options.FilterSubscriptions(remote);

                    if (!subscriptions.Any())
                    {
                        Console.WriteLine("No subscriptions found matching the specified criteria.");
                        return Constants.ErrorCode;
                    }

                    subscriptionsToDelete.AddRange(subscriptions);
                }

                if (!noConfirm)
                {
                    // Print out the list of subscriptions about to be triggered.
                    Console.WriteLine($"Will delete the following {subscriptionsToDelete.Count} subscriptions...");
                    foreach (var subscription in subscriptionsToDelete)
                    {
                        Console.WriteLine($"  {UxHelpers.GetSubscriptionDescription(subscription)}");
                    }

                    if (!UxHelpers.PromptForYesNo("Continue?"))
                    {
                        Console.WriteLine($"No subscriptions deleted, exiting.");
                        return Constants.ErrorCode;
                    }
                }

                Console.Write($"Deleting {subscriptionsToDelete.Count} subscriptions...{(noConfirm ? Environment.NewLine : "")}");
                foreach (var subscription in subscriptionsToDelete)
                {
                    // If noConfirm was passed, print out the subscriptions as we go
                    if (noConfirm)
                    {
                        Console.WriteLine($"  {UxHelpers.GetSubscriptionDescription(subscription)}");
                    }
                    await remote.DeleteSubscriptionAsync(subscription.Id.ToString());
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
                Logger.LogError(e, "Unexpected error while deleting subscriptions.");
                return Constants.ErrorCode;
            }
        }
    }
}
