// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

[Verb("set-repository-policies", HelpText = "Set Merge PRs options for the specific repository and branch")]
internal class SetRepositoryMergePoliciesCommandLineOptions : ConfigurationManagementCommandLineOptions<SetRepositoryMergePoliciesOperation>
{
    [Option("repo", HelpText = "Name of repository to set repository merge policies for.")]
    public string Repository { get; set; }

    [Option("branch", HelpText = "Name of repository to get repository merge policies for.")]
    public string Branch { get; set; }

    [Option("merge-prs", HelpText = "Whether Maestro should merge pull requests after all Maestro checks pass.")]
    public bool? MergePrs { get; set; }

    [Option("ignore-checks", Separator = ',', HelpText = "A comma-separated list of checks ignored when merging pull requests. Requires --merge-prs.")]
    public IReadOnlyCollection<string> IgnoreChecks { get; set; } = [];

    [Option('q', "quiet", HelpText = "Non-interactive mode (requires all elements to be passed on the command line).")]
    public bool Quiet { get; set; }
}
