// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using ProductConstructionService.ReproTool.Operations;

namespace ProductConstructionService.ReproTool.Options;

[Verb("repro", HelpText = "Locally reproduce a codeflow subscription in the maestro-auth-test org")]
internal class ReproOptions : Options
{
    [Option('s', "subscription", HelpText = "Subscription that's getting reproduced", Required = true)]
    public required string Subscription { get; init; }

    [Option("commit", HelpText = "Commit to flow. Use when not flowing a build. If neither commit or build is specified, the latest commit in the subscription's source repository is flown", Required = false)]
    public string? Commit { get; init; }

    [Option("buildId", HelpText = "BAR build ID to flow", Required = false)]
    public int? BuildId { get; init; }

    [Option("skip-cleanup", HelpText = "Don't delete the created resources if they're needed for further testing. This includes the channel, subscription and PR branches. False by default", Required = false)]
    public bool SkipCleanup { get; init; } = false;

    internal override Operation GetOperation(IServiceProvider sp)
        => ActivatorUtilities.CreateInstance<ReproOperation>(sp, this);
}
