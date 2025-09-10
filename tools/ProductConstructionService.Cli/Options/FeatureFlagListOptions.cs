// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using ProductConstructionService.Cli.Operations;

namespace ProductConstructionService.Cli.Options;

[Verb("feature-flag-list", HelpText = "List feature flags (optionally for a specific subscription)")]
internal class FeatureFlagListOptions : PcsStatusOptions
{
    [Option("subscription-id", Required = false, HelpText = "Subscription ID (optional - if not provided, lists all flags)")]
    public string? SubscriptionId { get; init; }

    public override IOperation GetOperation(IServiceProvider sp) => ActivatorUtilities.CreateInstance<FeatureFlagListOperation>(sp, this);
}