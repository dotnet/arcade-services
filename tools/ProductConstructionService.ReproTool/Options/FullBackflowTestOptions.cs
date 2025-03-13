// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using ProductConstructionService.ReproTool.Operations;

namespace ProductConstructionService.ReproTool.Options;

[Verb("backflow-test", HelpText = "Flows an existing VMR build to all repos in maestro-auth-test")]
internal class FullBackflowTestOptions : Options
{
    [Option("build", HelpText = "Real VMR build from which we'll take assets from", Required = true)]
    public required int BuildId { get; init; }

    [Option("target-branch", HelpText = "Target branch in all repos", Required = true)]
    public required string TargetBranch { get; init; }

    [Option("vmr-branch", HelpText = "Vmr branch from which to backflow", Required = true)]
    public required string VmrBranch { get; init; }

    [Option("commit", HelpText = "maestro-auth-test/dotnet commit to flow", Required = true)]
    public required string Commit { get; init; }

    internal override Operation GetOperation(IServiceProvider sp)
        => ActivatorUtilities.CreateInstance<FullBackflowTestOperation>(sp, this);
}
