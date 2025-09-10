// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using ProductConstructionService.Cli.Operations;

namespace ProductConstructionService.Cli.Options;

[Verb("feature-flag-available", HelpText = "List all available feature flags with descriptions")]
internal class FeatureFlagAvailableOptions : PcsStatusOptions
{
    public override IOperation GetOperation(IServiceProvider sp) => ActivatorUtilities.CreateInstance<FeatureFlagAvailableOperation>(sp, this);
}
