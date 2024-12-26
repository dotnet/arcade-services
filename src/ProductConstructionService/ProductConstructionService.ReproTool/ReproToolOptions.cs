// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;

namespace ProductConstructionService.ReproTool;

internal class ReproToolOptions
{
    [Option('s', "subscription", HelpText = "Subscription that's getting reproduced")]
    public required string Subscription { get; init; }

    [Option("github-token", HelpText = "GitHub token")]
    public required string GitHubToken { get; init; }
}
