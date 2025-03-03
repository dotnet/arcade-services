﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;

namespace ProductConstructionService.ReproTool;

internal class ReproToolOptions
{
    [Option('s', "subscription", HelpText = "Subscription that's getting reproduced", Required = true)]
    public required string Subscription { get; init; }

    [Option("github-token", HelpText = "GitHub token", Required = false)]
    public string? GitHubToken { get; set; }

    [Option("commit", HelpText = "Commit to flow. Use when not flowing a build. If neither commit or build is specified, the latest commit in the subscription's source repository is flown", Required = false)]
    public string? Commit { get; init; }

    [Option("buildId", HelpText = "BAR build ID to flow", Required = false)]
    public int? BuildId { get; init; }

    [Option("skip-cleanup", HelpText = "Don't delete the created resources if they're needed for further testing. This includes the channel, subscription and PR branches. False by default", Required = false)]
    public bool SkipCleanup { get; init; } = false;
}
