// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

[Verb("set-repository-policies", HelpText = "Set merge policies for the specific repository and branch")]
internal class SetRepositoryMergePoliciesCommandLineOptions : CommandLineOptions<SetRepositoryMergePoliciesOperation>
{
    [Option("repo", HelpText = "Name of repository to set repository merge policies for.")]
    public string Repository { get; set; }

    [Option("branch", HelpText = "Name of repository to get repository merge policies for.")]
    public string Branch { get; set; }

    [Option("standard-automerge", HelpText = "Use standard auto-merge policies. GitHub ignores WIP, license/cla and auto-merge.config.enforce checks," +
                                             "Azure DevOps ignores comment, reviewer and work item linking. Neither will auto-merge if changes are requested.")]
    public bool StandardAutoMergePolicies { get; set; }

    [Option("all-checks-passed", HelpText = "PR is automatically merged if there is at least one check and all are passed. " +
                                            "Optionally provide a comma separated list of ignored checks with --ignore-checks.")]
    public bool AllChecksSuccessfulMergePolicy { get; set; }

    [Option("ignore-checks", Separator = ',', HelpText = "For use with --all-checks-passed. A set of checks that are ignored.")]
    public IEnumerable<string> IgnoreChecks { get; set; }

    [Option("no-requested-changes", HelpText = "PR is not merged if there are changes requested or the PR has been rejected.")]
    public bool NoRequestedChangesMergePolicy { get; set; }

    [Option("no-downgrades", HelpText = "PR is not merged if there are version downgrades.")]
    public bool DontAutomergeDowngradesMergePolicy { get; set; }

    [Option("code-flow-check", HelpText = "PR is not merged if the ForwardFlow/BackFlow check fails for ForwardFlow/BackwardFlow subscription")]
    public bool CodeFlowCheckMergePolicy { get; set; }

    [Option('q', "quiet", HelpText = "Non-interactive mode (requires all elements to be passed on the command line).")]
    public bool Quiet { get; set; }
}
