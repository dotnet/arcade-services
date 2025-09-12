// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using ProductConstructionService.Cli.Operations;

namespace ProductConstructionService.Cli.Options;

[Verb("feature-flag-remove-from-all", HelpText = "Remove a feature flag from all subscriptions (admin operation)")]
internal class FeatureFlagRemoveFromAllOptions : PcsStatusOptions
{
    [Option("flag", Required = true, HelpText = "Feature flag name to remove from all subscriptions")]
    public string FlagName { get; init; } = string.Empty;

    public override IOperation GetOperation(IServiceProvider sp) => ActivatorUtilities.CreateInstance<FeatureFlagRemoveFromAllOperation>(sp, this);
}