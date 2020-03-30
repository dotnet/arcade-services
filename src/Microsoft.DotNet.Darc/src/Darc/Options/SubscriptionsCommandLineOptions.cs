// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CommandLine;
using System.Text.RegularExpressions;
using System;
using Microsoft.DotNet.Maestro.Client.Models;
using System.Collections.Generic;
using System.Linq;
using Microsoft.DotNet.DarcLib;
using System.Threading.Tasks;
using Microsoft.DotNet.Services.Utility;

namespace Microsoft.DotNet.Darc.Options
{
    abstract class SubscriptionsCommandLineOptions : CommandLineOptions
    {
        [Option("target-repo", HelpText = "Filter by target repo (matches substring unless --exact or --regex is passed).")]
        public string TargetRepository { get; set; }

        [Option("source-repo", HelpText = "Filter by source repo (matches substring unless --exact or --regex is passed).")]
        public string SourceRepository { get; set; }

        [Option("channel", HelpText = "Filter by source channel (matches substring unless --exact or --regex is passed).")]
        public string Channel { get; set; }

        [Option("target-branch", HelpText = "Filter by target branch (matches substring unless --exact or --regex is passed).")]
        public string TargetBranch { get; set; }

        [Option("frequencies", Separator = ',',
            HelpText = @"Filter by subscription update frequency. Typical values: ""everyWeek"", ""twiceDaily"", ""everyDay"", ""everyBuild"", ""none""")]
        public IEnumerable<string> Frequencies { get; set; }

        [Option("default-channel", HelpText = "Filter to subscriptions that target repo+branches that apply by default to the specified channel.")]
        public string DefaultChannelTarget { get; set; }

        [Option("exact", SetName = "exact", HelpText = "Match subscription parameters exactly (cannot be used with --regex).")]
        public bool ExactMatch { get; set; }

        [Option("regex", SetName = "regex", HelpText = "Match subscription parameters using regex (cannot be used with --exact).")]
        public bool RegexMatch { get; set; }

        [Option("disabled", HelpText = "Get only disabled subscriptions.")]
        public bool Disabled { get; set; }

        [Option("enabled", HelpText = "Get only enabled subscriptions.")]
        public bool Enabled { get; set; }

        [Option("batchable", HelpText = "Get only batchable subscriptions.")]
        public bool Batchable { get; set; }

        [Option("not-batchable", HelpText = "Get only non-batchable subscriptions.")]
        public bool NotBatchable { get; set; }

        [Option("ids", Separator = ',', HelpText = "Get only subscriptions with these ids.")]
        public IEnumerable<string> SubscriptionIds { get; set; }

        public async Task<IEnumerable<Subscription>> FilterSubscriptions(IRemote remote)
        {
            IEnumerable<DefaultChannel> defaultChannels = await remote.GetDefaultChannelsAsync();
            return (await remote.GetSubscriptionsAsync()).Where(subscription =>
            {
                return SubcriptionFilter(subscription, defaultChannels);
            });
        }

        public bool SubcriptionFilter(Subscription subscription, IEnumerable<DefaultChannel> defaultChannels)
        {
            return (SubscriptionParameterMatches(TargetRepository, subscription.TargetRepository) &&
                    SubscriptionParameterMatches(GitHelpers.NormalizeBranchName(TargetBranch), subscription.TargetBranch) &&
                    SubscriptionParameterMatches(SourceRepository, subscription.SourceRepository) &&
                    SubscriptionParameterMatches(Channel, subscription.Channel.Name) &&
                    SubscriptionEnabledParameterMatches(subscription) &&
                    SubscriptionBatchableParameterMatches(subscription) &&
                    SubscriptionIdsParameterMatches(subscription) &&
                    SubscriptionFrequenciesParameterMatches(subscription) &&
                    SubscriptionDefaultChannelTargetParameterMatches(subscription, defaultChannels));
        }

        public bool SubscriptionEnabledParameterMatches(Subscription subscription)
        {
            return (Enabled && subscription.Enabled) ||
                   (Disabled && !subscription.Enabled) ||
                   (!Enabled && !Disabled);
        }

        public bool SubscriptionBatchableParameterMatches(Subscription subscription)
        {
            return (Batchable && subscription.Policy.Batchable) ||
                   (NotBatchable && !subscription.Policy.Batchable) ||
                   (!Batchable && !NotBatchable);
        }

        public bool SubscriptionIdsParameterMatches(Subscription subscription)
        {
            return !SubscriptionIds.Any() || SubscriptionIds.Any(id => id.Equals(subscription.Id.ToString(), StringComparison.OrdinalIgnoreCase));
        }

        public bool SubscriptionFrequenciesParameterMatches(Subscription subscription)
        {
            return !Frequencies.Any() || Frequencies.Any(frequency => subscription.Policy.UpdateFrequency.ToString().Contains(frequency, StringComparison.OrdinalIgnoreCase));
        }

        public bool SubscriptionDefaultChannelTargetParameterMatches(Subscription subscription, IEnumerable<DefaultChannel> defaultChannels)
        {
            return string.IsNullOrEmpty(DefaultChannelTarget) || defaultChannels
                .Where(dc => SubscriptionParameterMatches(DefaultChannelTarget, dc.Channel.Name))
                .Any(dc => dc.Branch.Contains(subscription.TargetBranch, StringComparison.OrdinalIgnoreCase) && dc.Repository.Equals(subscription.TargetRepository, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        ///     Compare input command line options against subscription parameters
        /// </summary>
        /// <param name="inputParameter">Input command line option</param>
        /// <param name="subscriptionProperty">Subscription options.</param>
        /// <returns>True if it's a match, false otherwise.</returns>
        public bool SubscriptionParameterMatches(string inputParameter, string subscriptionProperty)
        {
            // If the parameter isn't set, it's a match
            if (string.IsNullOrEmpty(inputParameter))
            {
                return true;
            }

            // Compare properties ignoring case because branch, repo, etc. names cannot differ only by case.
            if (ExactMatch)
            {
                return inputParameter.Equals(subscriptionProperty, StringComparison.OrdinalIgnoreCase);
            }

            if (RegexMatch && Regex.IsMatch(subscriptionProperty, inputParameter, RegexOptions.IgnoreCase))
            {
                return true;
            }
            else
            {
                return subscriptionProperty.Contains(inputParameter, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Determine whether the set of input options has any valid filters.
        /// </summary>
        /// <returns>True if there are valid filters, false otherwise.</returns>
        public bool HasAnyFilters()
        {
            return !string.IsNullOrEmpty(TargetRepository) ||
                   !string.IsNullOrEmpty(TargetBranch) ||
                   !string.IsNullOrEmpty(SourceRepository) ||
                   !string.IsNullOrEmpty(Channel) ||
                   Frequencies.Any() ||
                   !string.IsNullOrEmpty(DefaultChannelTarget) ||
                   Disabled ||
                   Enabled ||
                   Batchable ||
                   NotBatchable ||
                   SubscriptionIds.Any();
        }
    }
}
