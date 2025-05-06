// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using CommandLine;
using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Models.PopUps;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Microsoft.DotNet.Darc.Operations;

internal class UpdateSubscriptionOperation : Operation
{
    private readonly UpdateSubscriptionCommandLineOptions _options;
    private readonly IBarApiClient _barClient;
    private readonly IGitRepoFactory _gitRepoFactory;
    private readonly ILogger<UpdateSubscriptionOperation> _logger;

    public UpdateSubscriptionOperation(
        UpdateSubscriptionCommandLineOptions options,
        IBarApiClient barClient,
        IGitRepoFactory gitRepoFactory,
        ILogger<UpdateSubscriptionOperation> logger)
    {
        _options = options;
        _barClient = barClient;
        _gitRepoFactory = gitRepoFactory;
        _logger = logger;
    }

    /// <summary>
    /// Implements the 'update-subscription' operation
    /// </summary>
    public override async Task<int> ExecuteAsync()
    {
        // First, try to get the subscription. If it doesn't exist the call will throw and the exception will be
        // caught by `RunOperation`
        Subscription subscription = await _barClient.GetSubscriptionAsync(_options.Id);

        var suggestedRepos = _barClient.GetSubscriptionsAsync();
        var suggestedChannels = _barClient.GetChannelsAsync();

        string channel = subscription.Channel.Name;
        string sourceRepository = subscription.SourceRepository;
        string updateFrequency = subscription.Policy.UpdateFrequency.ToString();
        bool batchable = subscription.Policy.Batchable;
        bool enabled = subscription.Enabled;
        string failureNotificationTags = subscription.PullRequestFailureNotificationTags;
        List<MergePolicy> mergePolicies = subscription.Policy.MergePolicies;
        bool sourceEnabled = subscription.SourceEnabled;
        List<string> excludedAssets = [..subscription.ExcludedAssets];
        string sourceDirectory = subscription.SourceDirectory;
        string targetDirectory = subscription.TargetDirectory;

        if (UpdatingViaCommandLine())
        {
            if (_options.IgnoreChecks.Any() && !_options.AllChecksSuccessfulMergePolicy && !_options.StandardAutoMergePolicies)
            {
                _logger.LogError("--ignore-checks must be combined with --all-checks-passed or --standard-automerge");
                return Constants.ErrorCode;
            }
            if (_options.CodeFlowCheckMergePolicy && !sourceEnabled)
            {
                _logger.LogError("--code-flow-check can only be used with source-enabled subscriptions");
                return Constants.ErrorCode;
            }

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
                    _logger.LogError($"Unknown update frequency '{_options.UpdateFrequency}'. Available options: {string.Join(',', Constants.AvailableFrequencies)}");
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

            if (_options.SourceEnabled.HasValue)
            {
                sourceEnabled = _options.SourceEnabled.Value;
            }

            if (_options.SourceDirectory != null)
            {
                sourceDirectory = _options.SourceDirectory;
            }

            if (_options.TargetDirectory != null)
            {
                targetDirectory = _options.TargetDirectory;
            }

            if (_options.ExcludedAssets != null)
            {
                excludedAssets = [.._options.ExcludedAssets.Split(';', StringSplitOptions.RemoveEmptyEntries)];
            }

            if (!_options.UpdateMergePolicies && UpdatingMergePoliciesViaCommandLine())
            {
                mergePolicies = [];
            }

            // Parse the merge policies
            if (_options.AllChecksSuccessfulMergePolicy)
            {
                AddMergePolicyWithIgnoreChecksIfMissing(mergePolicies, MergePolicyConstants.AllCheckSuccessfulMergePolicyName);
            }

            if (_options.NoRequestedChangesMergePolicy && !mergePolicies.Any(p => p.Name == MergePolicyConstants.NoRequestedChangesMergePolicyName))
            {
                mergePolicies.Add(
                    new MergePolicy
                    {
                        Name = MergePolicyConstants.NoRequestedChangesMergePolicyName,
                        Properties = []
                    });
            }

            if (_options.DontAutomergeDowngradesMergePolicy && !mergePolicies.Any(p => p.Name == MergePolicyConstants.DontAutomergeDowngradesPolicyName))
            {
                mergePolicies.Add(
                    new MergePolicy
                    {
                        Name = MergePolicyConstants.DontAutomergeDowngradesPolicyName,
                        Properties = []
                    });
            }

            if (_options.StandardAutoMergePolicies)
            {
                AddMergePolicyWithIgnoreChecksIfMissing(mergePolicies, MergePolicyConstants.StandardMergePolicyName);
            }

            if (_options.ValidateCoherencyCheckMergePolicy && !mergePolicies.Any(p => p.Name == MergePolicyConstants.ValidateCoherencyMergePolicyName))
            {
                mergePolicies.Add(
                    new MergePolicy
                    {
                        Name = MergePolicyConstants.ValidateCoherencyMergePolicyName,
                        Properties = []
                    });
            }

            if (_options.CodeFlowCheckMergePolicy && !mergePolicies.Any(p => p.Name == MergePolicyConstants.CodeflowMergePolicyName))
            {
                if (_options.StandardAutoMergePolicies)
                {
                    _logger.LogInformation("Code flow check merge policy is already included in standard auto-merge policies. Skipping");
                }
                else
                {
                    mergePolicies.Add(
                        new MergePolicy
                        {
                            Name = MergePolicyConstants.CodeflowMergePolicyName,
                            Properties = []
                        });
                }
            }

            if (_options.Batchable.HasValue && _options.Batchable.Value && mergePolicies.Count > 0)
            {
                _logger.LogError("Batchable subscriptions cannot be combined with merge policies. Merge policies are specified at a repository+branch level.");
                return Constants.ErrorCode;
            }
        }
        else
        {
            var updateSubscriptionPopUp = new UpdateSubscriptionPopUp(
                "update-subscription/update-subscription-todo",
                _options.ForceCreation,
                _gitRepoFactory,
                _logger,
                subscription,
                (await suggestedChannels).Select(suggestedChannel => suggestedChannel.Name),
                (await suggestedRepos).SelectMany(subs => new List<string> { subscription.SourceRepository, subscription.TargetRepository }).ToHashSet(),
                Constants.AvailableFrequencies,
                Constants.AvailableMergePolicyYamlHelp,
                subscription.PullRequestFailureNotificationTags ?? string.Empty,
                sourceEnabled,
                sourceDirectory,
                targetDirectory,
                excludedAssets);

            var uxManager = new UxManager(_options.GitLocation, _logger);

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
            sourceEnabled = updateSubscriptionPopUp.SourceEnabled;
            sourceDirectory = updateSubscriptionPopUp.SourceDirectory;
            targetDirectory = updateSubscriptionPopUp.TargetDirectory;
            excludedAssets = [..updateSubscriptionPopUp.ExcludedAssets];
        }

        if (excludedAssets.Any() && !sourceEnabled)
        {
            Console.WriteLine("Asset exclusion only works for source-enabled subscriptions");
            return Constants.ErrorCode;
        }

        try
        {
            var subscriptionToUpdate = new SubscriptionUpdate
            {
                ChannelName = channel ?? subscription.Channel.Name,
                SourceRepository = sourceRepository ?? subscription.SourceRepository,
                Enabled = enabled,
                Policy = subscription.Policy,
                PullRequestFailureNotificationTags = failureNotificationTags,
                SourceEnabled = sourceEnabled,
                ExcludedAssets = excludedAssets,
                SourceDirectory = sourceDirectory,
                TargetDirectory = targetDirectory,
            };

            subscriptionToUpdate.Policy.Batchable = batchable;
            subscriptionToUpdate.Policy.UpdateFrequency = Enum.Parse<UpdateFrequency>(updateFrequency, true);

            subscriptionToUpdate.Policy.MergePolicies = mergePolicies;

            var updatedSubscription = await _barClient.UpdateSubscriptionAsync(
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
                    await _barClient.TriggerSubscriptionAsync(updatedSubscription.Id);
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
            _logger.LogError($"Failed to update subscription: {e.Response.Content}");
            return Constants.ErrorCode;
        }
        catch (Exception e)
        {
            _logger.LogError(e, $"Failed to update subscription.");
            return Constants.ErrorCode;
        }
    }

    // If any specific values come from the command line, we'll skip the popup.
    // This enables bulk update for users who have many subscriptions, as the text-editor approach can be slow for them.
    private bool UpdatingViaCommandLine()
        => _options.Channel != null
           || _options.SourceRepoUrl != null
           || _options.Batchable != null
           || _options.UpdateFrequency != null
           || _options.Enabled != null
           || _options.FailureNotificationTags != null
           || _options.SourceEnabled != null
           || _options.SourceDirectory != null
           || _options.ExcludedAssets != null
           || UpdatingMergePoliciesViaCommandLine();

    private bool UpdatingMergePoliciesViaCommandLine()
        => _options.AllChecksSuccessfulMergePolicy
           || _options.NoRequestedChangesMergePolicy
           || _options.DontAutomergeDowngradesMergePolicy
           || _options.StandardAutoMergePolicies
           || _options.ValidateCoherencyCheckMergePolicy
           || _options.CodeFlowCheckMergePolicy;

    private IEnumerable<string> GetExistingIgnoreChecks(MergePolicy mergePolicy) => mergePolicy
                    .Properties
                    .GetValueOrDefault(MergePolicyConstants.IgnoreChecksMergePolicyPropertyName)?
                    .ToObject<IEnumerable<string>>()
                    ?? [];

    private void AddMergePolicyWithIgnoreChecksIfMissing(List<MergePolicy> mergePolicies, string policy)
    {
        var existingPolicy = mergePolicies.FirstOrDefault(p => p.Name == policy);
        if (existingPolicy != null)
        {
            existingPolicy.Properties[MergePolicyConstants.IgnoreChecksMergePolicyPropertyName] =
                JToken.FromObject(_options.IgnoreChecks.Concat(GetExistingIgnoreChecks(existingPolicy)).Distinct());
        }
        else
        {
            mergePolicies.Add(
                new MergePolicy
                {
                    Name = MergePolicyConstants.AllCheckSuccessfulMergePolicyName,
                    Properties = new()
                    {
                        [MergePolicyConstants.IgnoreChecksMergePolicyPropertyName]
                            = JToken.FromObject(_options.IgnoreChecks)
                    }
                });
        }
    }
}
