// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using ProductConstructionService.ReproTool.Operations;

namespace ProductConstructionService.ReproTool.Options;

[Verb("flow-commit", HelpText = "Flows the given branch between a repo and a VMR (both in maestro-auth-test) using local PCS")]
internal class FlowCommitOptions : Options
{
    [Option("source-repo", HelpText = "Repo (full URI) to flow the commit from (must be under maestro-auth-test/)", Required = true)]
    public required string SourceRepository { get; init; }

    [Option("source-branch", HelpText = "Either --source-branch or --source-commit is required. Branch whose commit will be flown. Ignored if source-commit is provided.", Required = false)]
    public string? SourceBranch { get; init; }

    [Option("source-commit", HelpText = "Either --source-branch or --source-commit is required. Source commit that will be flown.", Required = false)]
    public string? SourceCommit { get; init; }

    [Option("target-repo", HelpText = "Repo (full URI) to flow the commit to (must be under maestro-auth-test/)", Required = true)]
    public required string TargetRepository { get; init; }

    [Option("target-branch", HelpText = "Branch to flow the commit into", Required = true)]
    public required string TargetBranch { get; init; }

    [Option("packages", HelpText = "Name(s) of package(s) to include in the flown build", Required = false)]
    public IEnumerable<string> Packages { get; init; } = [];

    [Option("assets-from-build", HelpText = "A real build id from which to take assets. Shouldn't be provided if using the --packages option", Required = false)]
    public int RealBuildId { get; init; }

    [Option("skip-cleanup", HelpText = "Don't delete any of the created resources when finished", Required = false)]
    public bool SkipCleanup { get; init; }

    internal override Operation GetOperation(IServiceProvider sp)
        => ActivatorUtilities.CreateInstance<FlowCommitOperation>(sp, this);
}
