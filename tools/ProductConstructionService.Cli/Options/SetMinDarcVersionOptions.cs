// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using ProductConstructionService.Cli.Operations;
using Tools.Cli.Core;

namespace ProductConstructionService.Cli.Options;

[Verb("set-min-darc-version", HelpText = "Set the minimum required darc client version")]
internal class SetMinDarcVersionOptions : PcsApiOptions
{
    [Value(0, MetaName = "version", Required = true, HelpText = "The minimum required darc client version (semver, e.g. 1.2.3)")]
    public required string Version { get; init; }

    public override IOperation GetOperation(IServiceProvider sp) => ActivatorUtilities.CreateInstance<SetMinDarcVersionOperation>(sp, this);
}
