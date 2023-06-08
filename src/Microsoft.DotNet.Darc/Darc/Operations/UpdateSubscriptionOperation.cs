// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Models.PopUps;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Darc.Operations;

class UpdateSubscriptionOperation : Operation
{
    readonly UpdateSubscriptionCommandLineOptions _options;

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

        string channel = subscription.Channel.Name;
        string sourceRepository = subscription.SourceRepository;
        string updateFrequency = subscription.Policy.UpdateFrequency.ToString();
        bool batchable = subscription.Policy.Batchable;
        bool enabled = subscription.Enabled;
        string failureNotificationTags = subscription.PullRequestFailureNotificationTags;
        List<MergePolicy> mergePolicies;


        if (UpdatingViaCommandLine())
        {
            if (_options.Channel != null)
            {
                channel = _options.Channel;
            }
            if (_options.SourceRepoUrl != null)
            {
                sourceRepository = _options.SourceRepoUrl;
            }
            if (_options.Batchable != null)
            {
                batchable = (bool) _options.Batchable;
            }
            if (_options.UpdateFrequency != null)
            {
                if (!Constants.AvailableFrequencies.Contains(_options.UpdateFrequency, StringComparer.OrdinalIgnoreCase))
                {
                    Logger.LogError($"Unknown update frequency '{_options.UpdateFrequency}'. Available options: {string.Join(',', Constants.AvailableFrequencies)}");
                    return 1;
                }
                updateFrequency = _options.UpdateFrequency;
            }
            if (_options.Enabled != null)
            {
                enabled = (bool) _options.Enabled;
            }
            if (_options.FailureNotificationTags != null)
            {
                failureNotificationTags = _options.FailureNotificationTags;
            }
            mergePolicies = subscription.Policy.MergePolicies.ToList();
        }
        else
        {
            UpdateSubscriptionPopUp updateSubscriptionPopUp = new UpdateSubscriptionPopUp(
                "update-subscription/update-subscription-todo",
                Logger,
                subscription,
                (await suggestedChannels).Select(suggestedChannel => suggestedChannel.Name),
                (await suggestedRepos).SelectMany(subs => new List<string> { subscription.SourceRepository, subscription.TargetRepository }).ToHashSet(),
                Constants.AvailableFrequencies,
                Constants.AvailableMergePolicyYamlHelp,
                subscription.PullRequestFailureNotificationTags ?? string.Empty);

            UxManager uxManager = new UxManager(_options.GitLocation, Logger);

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
            failureNotificationTags = updateSubscriptionPopUp.FailureNotificationTags;
            mergePolicies = updateSubscriptionPopUp.MergePolicies;
        }
        try
        {
            SubscriptionUpdate subscriptionToUpdate = new SubscriptionUpdate
            {
                ChannelName = channel ?? subscription.Channel.Name,
                SourceRepository = sourceRepository ?? subscription.SourceRepository,
                Enabled = enabled,
                Policy = subscription.Policy,
                PullRequestFailureNotificationTags = failureNotificationTags
            };
            subscriptionToUpdate.Policy.Batchable = batchable;
            subscriptionToUpdate.Policy.UpdateFrequency = Enum.Parse<UpdateFrequency>(updateFrequency, true);
            subscriptionToUpdate.Policy.MergePolicies = mergePolicies?.ToImmutableList();

            var updatedSubscription = await remote.UpdateSubscriptionAsync(
                _options.Id,
                subscriptionToUpdate);

            Console.WriteLine($"Successfully updated subscription with id '{updatedSubscription.Id}'.");

            // Determine whether the subscription should be triggered.
            if (!_options.NoTriggerOnUpdate)
            {
                bool triggerAutomatically = _options.TriggerOnUpdate;
                // Determine whether we should prompt if the user hasn't explicitly
                // said one way or another. We shouldn't prompt if nothing changes or
                // if non-interesting options have changed
                if (!triggerAutomatically &&
                    ((subscriptionToUpdate.ChannelName != subscription.Channel.Name) ||
                     (subscriptionToUpdate.SourceRepository != subscription.SourceRepository) ||
                     (subscriptionToUpdate.Enabled.Value && !subscription.Enabled) ||
                     (subscriptionToUpdate.Policy.UpdateFrequency != UpdateFrequency.None && subscriptionToUpdate.Policy.UpdateFrequency !=
                         subscription.Policy.UpdateFrequency)))
                {
                    triggerAutomatically = UxHelpers.PromptForYesNo("Trigger this subscription immediately?");
                }

                if (triggerAutomatically)
                {
                    await remote.TriggerSubscriptionAsync(updatedSubscription.Id.ToString());
                    Console.WriteLine($"Subscription '{updatedSubscription.Id}' triggered.");
                }
            }

            return Constants.SuccessCode;
        }
        catch (AuthenticationException e)
        {
            Console.WriteLine(e.Message);
            return Constants.ErrorCode;
        }
        catch (RestApiException e) when (e.Response.Status == (int) System.Net.HttpStatusCode.BadRequest)
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

    private bool UpdatingViaCommandLine()
    {
        // If any specific values come from the command line, we'll skip the popup.
        // This enables bulk update for users who have many subscriptions, as the text-editor approach can be slow for them.
        return _options.Channel != null ||
               _options.SourceRepoUrl != null ||
               _options.Batchable != null ||
               _options.UpdateFrequency != null ||
               _options.Enabled != null || 
               _options.FailureNotificationTags != null;
    }
}
