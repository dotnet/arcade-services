// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using ProductConstructionService.Cli.Operations;

namespace ProductConstructionService.Cli.Options;

[Verb("feature-flag-list-by-flag", HelpText = "List all subscriptions that have a specific feature flag set")]
internal class FeatureFlagListByFlagOptions : PcsStatusOptions
{
    [Option("flag", Required = true, HelpText = "Feature flag name to search for")]
    public string FlagName { get; init; } = string.Empty;

    public override IOperation GetOperation(IServiceProvider sp) => ActivatorUtilities.CreateInstance<FeatureFlagListByFlagOperation>(sp, this);
}