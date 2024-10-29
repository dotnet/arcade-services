// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using ProductConstructionService.Cli.Operations;

namespace ProductConstructionService.Cli.Options;

[Verb("get-status", HelpText = "Get PCS status")]
internal class GetPcsStatusOptions : PcsStatusOptions
{
    public override IOperation GetOperation(IServiceProvider sp) => ActivatorUtilities.CreateInstance<GetPcsStatusOperation>(sp);
}
