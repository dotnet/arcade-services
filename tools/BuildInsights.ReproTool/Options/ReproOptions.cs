// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using BuildInsights.ReproTool.Operations;

namespace BuildInsights.ReproTool.Options;

[Verb("repro", HelpText = "Replay an existing GitHub pull request into a Build Insights repro PR")]
internal class ReproOptions : Options
{
    [Option('p', "pr", HelpText = "GitHub pull request URI to reproduce.", Required = true)]
    public required string PullRequestUri { get; init; }

    [Option("skip-cleanup", HelpText = "Keep the repro branch and PR open after the replay finishes.", Required = false)]
    public bool SkipCleanup { get; init; }

    [Option("timeout-minutes", HelpText = "How long to wait for the Build Insights repro flow to finish.", Required = false)]
    public int TimeoutMinutes { get; init; } = 30;

    internal override Operation GetOperation(IServiceProvider sp)
        => ActivatorUtilities.CreateInstance<ReproOperation>(sp, this);
}
