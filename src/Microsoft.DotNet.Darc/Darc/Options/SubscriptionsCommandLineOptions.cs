// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommandLine;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.DotNet.Services.Utility;

namespace Microsoft.DotNet.Darc.Options;

internal abstract class SubscriptionsCommandLineOptions<T> : ConfigurationManagementCommandLineOptions<T> where T : Operation
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
        HelpText = @"Filter by subscription update frequency. Typical values: ""everyMonth"", ""everyTwoWeeks"", ""everyWeek"", ""twiceDaily"", ""everyDay"", ""everyBuild"", ""none""")]
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

    [Option("source-enabled", HelpText = "Get only source-enabled (VMR code flow) subscriptions.")]
    public bool? SourceEnabled { get; set; }

    [Option("source-directory", HelpText = "Get only source-enabled (VMR code flow) subscriptions that come from a given VMR directory.")]
    public string SourceDirectory { get; set; }

    [Option("target-directory", HelpText = "Get only source-enabled (VMR code flow) subscriptions that target a given VMR directory.")]
    public string TargetDirectory { get; set; }

    [Option("batchable", HelpText = "Get only batchable subscriptions.")]
    public bool Batchable { get; set; }

    [Option("not-batchable", HelpText = "Get only non-batchable subscriptions.")]
    public bool NotBatchable { get; set; }

    [Option("ids", Separator = ',', HelpText = "Get only subscriptions with these ids.")]
    public IEnumerable<string> SubscriptionIds { get; set; }

    public async Task<IEnumerable<Subscription>> FilterSubscriptions(IBarApiClient barClient)
    {
        IEnumerable<DefaultChannel> defaultChannels = await barClient.GetDefaultChannelsAsync();
        return (await barClient.GetSubscriptionsAsync()).Where(subscription =>
        {
            return SubcriptionFilter(subscription, defaultChannels);
        });
    }

    public bool SubcriptionFilter(Subscription subscription, IEnumerable<DefaultChannel> defaultChannels)
    {
        return SubscriptionParameterMatches(TargetRepository, subscription.TargetRepository) &&
               SubscriptionParameterMatches(GitHelpers.NormalizeBranchName(TargetBranch), subscription.TargetBranch) &&
               SubscriptionParameterMatches(SourceRepository, subscription.SourceRepository) &&
               SubscriptionParameterMatches(Channel, subscription.Channel.Name) &&
               SubscriptionEnabledParameterMatches(subscription) &&
               SubscriptionSourceEnabledParameterMatches(subscription) &&
               SubscriptionSourceDirectoryParameterMatches(subscription) &&
               SubscriptionBatchableParameterMatches(subscription) &&
               SubscriptionIdsParameterMatches(subscription) &&
               SubscriptionFrequenciesParameterMatches(subscription) &&
               SubscriptionDefaultChannelTargetParameterMatches(subscription, defaultChannels);
    }

    public bool SubscriptionEnabledParameterMatches(Subscription subscription)
    {
        return (Enabled && subscription.Enabled) ||
               (Disabled && !subscription.Enabled) ||
               (!Enabled && !Disabled);
    }

    public bool SubscriptionSourceEnabledParameterMatches(Subscription subscription)
    {
        return !SourceEnabled.HasValue || subscription.SourceEnabled == SourceEnabled;
    }

    public bool SubscriptionSourceDirectoryParameterMatches(Subscription subscription)
    {
        // If the parameter isn't set, it's a match
        if (SourceDirectory == null)
        {
            return true;
        }

        return subscription.SourceDirectory == SourceDirectory;
    }

    public bool SubscriptionTargetDirectoryParameterMatches(Subscription subscription)
    {
        // If the parameter isn't set, it's a match
        if (TargetDirectory == null)
        {
            return true;
        }

        return subscription.TargetDirectory == TargetDirectory;
    }

    public bool SubscriptionBatchableParameterMatches(Subscription subscription)
    {
        return (Batchable && subscription.Policy.Batchable) ||
               (NotBatchable && !subscription.Policy.Batchable) ||
               (!Batchable && !NotBatchable);
    }

    public bool SubscriptionIdsParameterMatches(Subscription subscription)
        => SubscriptionIds is null
        || !SubscriptionIds.Any()
        || SubscriptionIds.Any(id => id.Equals(subscription.Id.ToString(), StringComparison.OrdinalIgnoreCase));

    public bool SubscriptionFrequenciesParameterMatches(Subscription subscription)
        => Frequencies is null
        || !Frequencies.Any()
        || Frequencies.Any(frequency => subscription.Policy.UpdateFrequency.ToString().Contains(frequency, StringComparison.OrdinalIgnoreCase));

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

        if (RegexMatch)
        {
            return Regex.IsMatch(subscriptionProperty, inputParameter, RegexOptions.IgnoreCase);
        }

        return subscriptionProperty.Contains(inputParameter, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determine whether the set of input options has any valid filters.
    /// </summary>
    /// <returns>True if there are valid filters, false otherwise.</returns>
    public bool HasAnyFilters() =>
        !string.IsNullOrEmpty(TargetRepository)
        || !string.IsNullOrEmpty(TargetBranch)
        || !string.IsNullOrEmpty(SourceRepository)
        || !string.IsNullOrEmpty(Channel)
        || Frequencies.Any()
        || !string.IsNullOrEmpty(DefaultChannelTarget)
        || Disabled
        || Enabled
        || SourceEnabled.HasValue
        || !string.IsNullOrEmpty(SourceDirectory)
        || !string.IsNullOrEmpty(TargetDirectory)
        || Batchable
        || NotBatchable
        || SubscriptionIds.Any();
}
