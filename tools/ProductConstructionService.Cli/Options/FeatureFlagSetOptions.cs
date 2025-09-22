// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using ProductConstructionService.Cli.Operations;

namespace ProductConstructionService.Cli.Options;

[Verb("feature-flag-set", HelpText = "Set a feature flag for a subscription")]
internal class FeatureFlagSetOptions : PcsStatusOptions
{
    [Option("subscription-id", Required = true, HelpText = "Subscription ID")]
    public required string SubscriptionId { get; init; }

    [Option("flag", Required = true, HelpText = "Feature flag name")]
    public required string FlagName { get; init; }

    [Option("value", Required = true, HelpText = "Feature flag value")]
    public required string Value { get; init; }

    [Option("expiry-days", Required = false, HelpText = "Number of days until the flag expires")]
    public int? ExpiryDays { get; init; }

    public override IOperation GetOperation(IServiceProvider sp) => ActivatorUtilities.CreateInstance<FeatureFlagSetOperation>(sp, this);
}