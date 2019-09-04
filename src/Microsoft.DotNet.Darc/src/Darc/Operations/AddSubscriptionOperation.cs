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
using Newtonsoft.Json.Linq;

namespace Microsoft.DotNet.Darc.Operations
{
    class AddSubscriptionOperation : Operation
    {
        AddSubscriptionCommandLineOptions _options;

        public AddSubscriptionOperation(AddSubscriptionCommandLineOptions options)
            : base(options)
        {
            _options = options;
        }

        /// <summary>
        /// Implements the 'add-subscription' operation
        /// </summary>
        /// <param name="options"></param>
        public override async Task<int> ExecuteAsync()
        {
            IRemote remote = RemoteFactory.GetBarOnlyRemote(_options, Logger);

            if (_options.IgnoreChecks.Count() > 0 && !_options.AllChecksSuccessfulMergePolicy)
            {
                Console.WriteLine($"--ignore-checks must be combined with --all-checks-passed");
                return Constants.ErrorCode;
            }

            // Parse the merge policies
            List<MergePolicy> mergePolicies = new List<MergePolicy>();
            if (_options.NoExtraCommitsMergePolicy)
            {
                mergePolicies.Add(
                    new MergePolicy
                    {
                        Name = Constants.NoExtraCommitsMergePolicyName
                    });
            }

            if (_options.AllChecksSuccessfulMergePolicy)
            {
                mergePolicies.Add(
                    new MergePolicy
                    {
                        Name = Constants.AllCheckSuccessfulMergePolicyName,
                        Properties = ImmutableDictionary.Create<string, JToken>()
                            .Add(Constants.IgnoreChecksMergePolicyPropertyName, JToken.FromObject(_options.IgnoreChecks))
                    });
            }

            if (_options.NoRequestedChangesMergePolicy)
            {
                mergePolicies.Add(
                    new MergePolicy
                    {
                        Name = Constants.NoRequestedChangesMergePolicyName,
                        Properties = ImmutableDictionary.Create<string, JToken>()
                    });
            }

            if (_options.StandardAutoMergePolicies)
            {
                mergePolicies.Add(
                    new MergePolicy
                    {
                        Name = Constants.StandardMergePolicyName,
                        Properties = ImmutableDictionary.Create<string, JToken>()
                    });
            }

            if (_options.Batchable && mergePolicies.Count > 0)
            {
                Console.WriteLine("Batchable subscriptions cannot be combined with merge policies. " +
                    "Merge policies are specified at a repository+branch level.");
                return Constants.ErrorCode;
            }

            string channel = _options.Channel;
            string sourceRepository = _options.SourceRepository;
            string targetRepository = _options.TargetRepository;
            string targetBranch = _options.TargetBranch;
            string updateFrequency = _options.UpdateFrequency;
            bool batchable = _options.Batchable;

            // If in quiet (non-interactive mode), ensure that all options were passed, then
            // just call the remote API
            if (_options.Quiet && !_options.ReadStandardIn)
            {
                if (string.IsNullOrEmpty(channel) ||
                    string.IsNullOrEmpty(sourceRepository) ||
                    string.IsNullOrEmpty(targetRepository) ||
                    string.IsNullOrEmpty(targetBranch) ||
                    string.IsNullOrEmpty(updateFrequency) ||
                    !Constants.AvailableFrequencies.Contains(updateFrequency, StringComparer.OrdinalIgnoreCase))
                {
                    Logger.LogError($"Missing input parameters for the subscription. Please see command help or remove --quiet/-q for interactive mode");
                    return Constants.ErrorCode;
                }
            }
            else
            {
                // Grab existing subscriptions to get suggested values.
                // TODO: When this becomes paged, set a max number of results to avoid
                // pulling too much.
                var suggestedRepos = remote.GetSubscriptionsAsync();
                var suggestedChannels = remote.GetChannelsAsync();

                // Help the user along with a form.  We'll use the API to gather suggested values
                // from existing subscriptions based on the input parameters.
                AddSubscriptionPopUp addSubscriptionPopup =
                    new AddSubscriptionPopUp("add-subscription/add-subscription-todo",
                                             Logger,
                                             channel,
                                             sourceRepository,
                                             targetRepository,
                                             targetBranch,
                                             updateFrequency,
                                             batchable,
                                             mergePolicies,
                                             (await suggestedChannels).Select(suggestedChannel => suggestedChannel.Name),
                                             (await suggestedRepos).SelectMany(subscription => new List<string> {subscription.SourceRepository, subscription.TargetRepository }).ToHashSet(),
                                             Constants.AvailableFrequencies,
                                             Constants.AvailableMergePolicyYamlHelp);

                UxManager uxManager = new UxManager(_options.GitLocation, Logger);
                int exitCode = _options.ReadStandardIn ? uxManager.ReadFromStdIn(addSubscriptionPopup) : uxManager.PopUp(addSubscriptionPopup);
                if (exitCode != Constants.SuccessCode)
                {
                    return exitCode;
                }
                channel = addSubscriptionPopup.Channel;
                sourceRepository = addSubscriptionPopup.SourceRepository;
                targetRepository = addSubscriptionPopup.TargetRepository;
                targetBranch = addSubscriptionPopup.TargetBranch;
                updateFrequency = addSubscriptionPopup.UpdateFrequency;
                mergePolicies = addSubscriptionPopup.MergePolicies;
                batchable = addSubscriptionPopup.Batchable; 
            }

            try
            {
                // If we are about to add a batchable subscription and the merge policies are empty for the
                // target repo/branch, warn the user.
                if (batchable)
                {
                    var existingMergePolicies = await remote.GetRepositoryMergePoliciesAsync(targetRepository, targetBranch);
                    if (!existingMergePolicies.Any())
                    {
                        Console.WriteLine("Warning: Batchable subscription doesn't have any repository merge policies. " +
                            "PRs will not be auto-merged.");
                        Console.WriteLine($"Please use 'darc set-repository-policies --repo {targetRepository} --branch {targetBranch}' " +
                            $"to set policies.{Environment.NewLine}");
                    }
                }

                var newSubscription = await remote.CreateSubscriptionAsync(channel,
                                                                           sourceRepository,
                                                                           targetRepository,
                                                                           targetBranch,
                                                                           updateFrequency,
                                                                           batchable,
                                                                           mergePolicies);
                Console.WriteLine($"Successfully created new subscription with id '{newSubscription.Id}'.");
                return Constants.SuccessCode;
            }
            catch (RestApiException e) when (e.Response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                // Could have been some kind of validation error (e.g. channel doesn't exist)
                Logger.LogError($"Failed to create subscription: {e.Response.Content}");
                return Constants.ErrorCode;
            }
            catch (Exception e)
            {
                Logger.LogError(e, $"Failed to create subscription.");
                return Constants.ErrorCode;
            }
        }
    }
}
