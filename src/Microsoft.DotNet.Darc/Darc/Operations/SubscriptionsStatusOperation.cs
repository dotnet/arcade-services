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

internal class SubscriptionsStatusOperation : Operation
{
    private readonly SubscriptionsStatusCommandLineOptions _options;
    private readonly IBarApiClient _barClient;
    private readonly ILogger<SubscriptionsStatusOperation> _logger;

    public SubscriptionsStatusOperation(
        SubscriptionsStatusCommandLineOptions options,
        IBarApiClient barClient,
        ILogger<SubscriptionsStatusOperation> logger)
    {
        _options = options;
        _barClient = barClient;
        _logger = logger;
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
            Console.WriteLine("Please specify either --enable or --disable");
            return Constants.ErrorCode;
        }

        string presentTenseStatusMessage = _options.Enable ? "enable" : "disable";
        string pastTenseStatusMessage = _options.Enable ? "enabled" : "disabled";
        string actionStatusMessage = _options.Enable ? "Enabling" : "Disabling";

        try
        {
            bool noConfirm = _options.NoConfirmation;
            List<Subscription> subscriptionsToEnableDisable = [];

            if (!string.IsNullOrEmpty(_options.Id))
            {
                // Look up subscription so we can print it later.
                try
                {
                    Subscription subscription = await _barClient.GetSubscriptionAsync(_options.Id);
                    subscriptionsToEnableDisable.Add(subscription);
                }
                catch (RestApiException e) when (e.Response.Status == (int)HttpStatusCode.NotFound)
                {
                    Console.WriteLine($"Subscription with id '{_options.Id}' was not found.");
                    return Constants.ErrorCode;
                }
            }
            else
            {
                if (!_options.HasAnyFilters())
                {
                    Console.WriteLine($"Please specify one or more filters to select which subscriptions should be {pastTenseStatusMessage} (see help).");
                    return Constants.ErrorCode;
                }

                IEnumerable<Subscription> subscriptions = await _options.FilterSubscriptions(_barClient);

                if (!subscriptions.Any())
                {
                    Console.WriteLine("No subscriptions found matching the specified criteria.");
                    return Constants.ErrorCode;
                }

                subscriptionsToEnableDisable.AddRange(subscriptions);
            }

            // Filter away subscriptions that already have the desired state:
            subscriptionsToEnableDisable = subscriptionsToEnableDisable.Where(s => s.Enabled != _options.Enable).ToList();

            if (!subscriptionsToEnableDisable.Any())
            {
                Console.WriteLine($"All subscriptions are already {pastTenseStatusMessage}.");
                return Constants.SuccessCode;
            }

            if (!noConfirm)
            {
                // Print out the list of subscriptions about to be enabled/disabled.
                Console.WriteLine($"Will {presentTenseStatusMessage} the following {subscriptionsToEnableDisable.Count} subscriptions...");
                foreach (var subscription in subscriptionsToEnableDisable)
                {
                    Console.WriteLine($"  {UxHelpers.GetSubscriptionDescription(subscription)}");
                }

                if (!UxHelpers.PromptForYesNo("Continue?"))
                {
                    Console.WriteLine($"No subscriptions {pastTenseStatusMessage}, exiting.");
                    return Constants.ErrorCode;
                }
            }

            Console.Write($"{actionStatusMessage} {subscriptionsToEnableDisable.Count} subscriptions...{(noConfirm ? Environment.NewLine : "")}");
            foreach (var subscription in subscriptionsToEnableDisable)
            {
                // If noConfirm was passed, print out the subscriptions as we go
                if (noConfirm)
                {
                    Console.WriteLine($"  {UxHelpers.GetSubscriptionDescription(subscription)}");
                }

                var subscriptionToUpdate = new SubscriptionUpdate
                {
                    ChannelName = subscription.Channel.Name,
                    SourceRepository = subscription.SourceRepository,
                    Enabled = _options.Enable,
                    Policy = subscription.Policy
                };
                subscriptionToUpdate.Policy.Batchable = subscription.Policy.Batchable;
                subscriptionToUpdate.Policy.UpdateFrequency = subscription.Policy.UpdateFrequency;
                subscriptionToUpdate.Policy.MergePolicies = subscription.Policy.MergePolicies;

                var updatedSubscription = await _barClient.UpdateSubscriptionAsync(
                    subscription.Id.ToString(),
                    subscriptionToUpdate);
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
            _logger.LogError(e, $"Unexpected error while {actionStatusMessage.ToLower()} subscriptions.");
            return Constants.ErrorCode;
        }
    }
}
