// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Darc.Operations;

internal class TriggerSubscriptionsOperation : Operation
{
    private readonly TriggerSubscriptionsCommandLineOptions _options;
    private readonly IBarApiClient _barClient;
    private readonly ILogger<TriggerSubscriptionsOperation> _logger;

    public TriggerSubscriptionsOperation(
        TriggerSubscriptionsCommandLineOptions options,
        IBarApiClient barClient,
        ILogger<TriggerSubscriptionsOperation> logger)
    {
        _options = options;
        _barClient = barClient;
        _logger = logger;
    }

    /// <summary>
    /// Triggers subscriptions
    /// </summary>
    /// <returns></returns>
    public override async Task<int> ExecuteAsync()
    {
        try
        {
            bool noConfirm = _options.NoConfirmation;
            List<Subscription> subscriptionsToTrigger = [];

            if (!string.IsNullOrEmpty(_options.Id))
            {
                // Look up subscription so we can print it later.
                try
                {
                    Subscription subscription = await _barClient.GetSubscriptionAsync(_options.Id);
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

                IEnumerable<Subscription> subscriptions = await _options.FilterSubscriptions(_barClient);

                if (!subscriptions.Any())
                {
                    Console.WriteLine("No subscriptions found matching the specified criteria.");
                    return Constants.ErrorCode;
                }

                subscriptionsToTrigger.AddRange(subscriptions);
            }

            if (_options.Build != 0)
            {
                var specificBuild = await _barClient.GetBuildAsync(_options.Build);
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
            List<Subscription> disabledSubscriptions = [.. subscriptionsToTrigger.Where(s => !s.Enabled)];
            subscriptionsToTrigger = [.. subscriptionsToTrigger.Where(s => s.Enabled)];

            if (disabledSubscriptions.Count != 0)
            {
                Console.WriteLine($"The following {disabledSubscriptions.Count} subscription(s) are disabled and will not be triggered");
                foreach (var subscription in disabledSubscriptions)
                {
                    Console.WriteLine($"  {UxHelpers.GetSubscriptionDescription(subscription)}");
                }
            }

            if (subscriptionsToTrigger.Count == 0)
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
                    await _barClient.TriggerSubscriptionAsync(subscription.Id, _options.Build, _options.Force);
                }
                else
                {
                    await _barClient.TriggerSubscriptionAsync(subscription.Id, _options.Force);
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
            _logger.LogError(e, "Unexpected error while triggering subscriptions.");
            return Constants.ErrorCode;
        }
    }
}
