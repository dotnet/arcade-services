// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using ProductConstructionService.ReproTool.Operations;

namespace ProductConstructionService.ReproTool.Options;

[Verb("flow-commit", HelpText = "Flows the given branch between a repo and a VMR (both in maestro-auth-test) using local PCS")]
internal class FlowCommitOptions : Options
{
    [Option("channel", HelpText = "Channel to use / create", Required = true)]
    public required string Channel { get; init; }

    [Option("source-repo", HelpText = "Repo (full URI) to flow the commit from (must be under maestro-auth-test/)", Required = true)]
    public required string SourceRepository { get; init; }

    [Option("source-branch", HelpText = "Branch whose commit will be flown", Required = true)]
    public required string SourceBranch { get; init; }

    [Option("target-repo", HelpText = "Repo (full URI) to flow the commit to (must be under maestro-auth-test/)", Required = true)]
    public required string TargetRepository { get; init; }

    [Option("target-branch", HelpText = "Branch to flow the commit into", Required = true)]
    public required string TargetBranch { get; init; }

    [Option("package", HelpText = "Name(s) of package(s) to include in the flown build", Required = false)]
    public IEnumerable<string> Packages { get; init; } = [];

    internal override Operation GetOperation(IServiceProvider sp)
        => ActivatorUtilities.CreateInstance<FlowCommitOperation>(sp, this);
}
