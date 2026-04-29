// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using ProductConstructionService.Cli.Operations;
using Tools.Cli.Core;

namespace ProductConstructionService.Cli.Options;

[Verb("clear-min-darc-version", HelpText = "Clear the minimum required darc client version")]
internal class ClearMinDarcVersionOptions : PcsApiOptions
{
    public override IOperation GetOperation(IServiceProvider sp) => ActivatorUtilities.CreateInstance<ClearMinDarcVersionOperation>(sp);
}
