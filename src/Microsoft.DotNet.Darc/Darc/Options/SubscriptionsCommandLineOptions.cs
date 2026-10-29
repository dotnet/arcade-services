// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.DotNet.Services.Utility;

namespace Microsoft.DotNet.Darc.Options;

internal abstract class SubscriptionsCommandLineOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T> : ConfigurationManagementCommandLineOptions<T> where T : Operation
{
    public string TargetRepository { get; set; }

    public string SourceRepository { get; set; }

    public string Channel { get; set; }

    public string TargetBranch { get; set; }

    public IEnumerable<string> Frequencies { get; set; }

    public string DefaultChannelTarget { get; set; }

    public bool ExactMatch { get; set; }

    public bool RegexMatch { get; set; }

    public bool Disabled { get; set; }

    public bool Enabled { get; set; }

    public bool? SourceEnabled { get; set; }

    public string SourceDirectory { get; set; }

    public string TargetDirectory { get; set; }

    public bool Batchable { get; set; }

    public bool NotBatchable { get; set; }

    public IEnumerable<string> SubscriptionIds { get; set; }

    protected override void Configure()
    {
        Add<string>("--target-repo", "Filter by target repo (matches substring unless --exact or --regex is passed).", value => TargetRepository = value);
        Add<string>("--source-repo", "Filter by source repo (matches substring unless --exact or --regex is passed).", value => SourceRepository = value);
        Add<string>("--channel", "Filter by source channel (matches substring unless --exact or --regex is passed).", value => Channel = value);
        Add<string>("--target-branch", "Filter by target branch (matches substring unless --exact or --regex is passed).", value => TargetBranch = value);
        AddSeparatedList(
            ',',
            "--frequencies",
            @"Filter by subscription update frequency. Typical values: ""everyMonth"", ""everyTwoWeeks"", ""everyWeek"", ""twiceDaily"", ""everyDay"", ""everyBuild"", ""none""",
            value => Frequencies = value);
        Add<string>("--default-channel", "Filter to subscriptions that target repo+branches that apply by default to the specified channel.", value => DefaultChannelTarget = value);
        var exactMatch = Add<bool>("--exact", "Match subscription parameters exactly (cannot be used with --regex).", value => ExactMatch = value);
        var regexMatch = Add<bool>("--regex", "Match subscription parameters using regex (cannot be used with --exact).", value => RegexMatch = value);
        Add<bool>("--disabled", "Get only disabled subscriptions.", value => Disabled = value);
        Add<bool>("--enabled", "Get only enabled subscriptions.", value => Enabled = value);
        Add<bool?>("--source-enabled", "Get only source-enabled (VMR code flow) subscriptions.", value => SourceEnabled = value);
        Add<string>("--source-directory", "Get only source-enabled (VMR code flow) subscriptions that come from a given VMR directory.", value => SourceDirectory = value);
        Add<string>("--target-directory", "Get only source-enabled (VMR code flow) subscriptions that target a given VMR directory.", value => TargetDirectory = value);
        Add<bool>("--batchable", "Get only batchable subscriptions.", value => Batchable = value);
        Add<bool>("--not-batchable", "Get only non-batchable subscriptions.", value => NotBatchable = value);
        AddSeparatedList(
            ',',
            "--ids",
            "Get only subscriptions with these ids.",
            value => SubscriptionIds = value);
        AddMutuallyExclusive(exactMatch, regexMatch);
        base.Configure();
    }

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
