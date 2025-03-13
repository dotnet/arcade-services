// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using ProductConstructionService.ReproTool.Operations;

namespace ProductConstructionService.ReproTool.Options;
[Verb("flat-flow-test", HelpText = "Test full flat flow in the maestro-auth-test org")]
internal class FlatFlowTestOptions : Options
{
    internal override Operation GetOperation(IServiceProvider sp)
        => ActivatorUtilities.CreateInstance<FlatFlowTestOperation>(sp);
}
