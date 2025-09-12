// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using ProductConstructionService.Cli.Operations;

namespace ProductConstructionService.Cli.Options;

[Verb("feature-flag-remove", HelpText = "Remove a feature flag for a subscription")]
internal class FeatureFlagRemoveOptions : PcsStatusOptions
{
    [Option("subscription-id", Required = true, HelpText = "Subscription ID")]
    public required string SubscriptionId { get; init; }

    [Option("flag", Required = true, HelpText = "Feature flag name")]
    public required string FlagName { get; init; }

    public override IOperation GetOperation(IServiceProvider sp) => ActivatorUtilities.CreateInstance<FeatureFlagRemoveOperation>(sp, this);
}