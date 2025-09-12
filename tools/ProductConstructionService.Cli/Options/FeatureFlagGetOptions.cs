// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using ProductConstructionService.Cli.Operations;

namespace ProductConstructionService.Cli.Options;

[Verb("feature-flag-get", HelpText = "Get feature flags for a subscription")]
internal class FeatureFlagGetOptions : PcsStatusOptions
{
    [Option("subscription-id", Required = true, HelpText = "Subscription ID")]
    public required string SubscriptionId { get; init; }

    [Option("flag", Required = false, HelpText = "Specific feature flag name (optional)")]
    public string? FlagName { get; init; }

    public override IOperation GetOperation(IServiceProvider sp) => ActivatorUtilities.CreateInstance<FeatureFlagGetOperation>(sp, this);
}