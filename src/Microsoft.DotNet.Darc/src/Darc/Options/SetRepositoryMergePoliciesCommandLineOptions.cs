// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;
using System.Collections.Generic;

namespace Microsoft.DotNet.Darc.Options
{
    [Verb("set-repository-policies", HelpText = "Set merge policies for the specific repository and branch")]
    internal class SetRepositoryMergePoliciesCommandLineOptions : CommandLineOptions
    {
        [Option("repo", HelpText = "Name of repository to set repository merge policies for.")]
        public string Repository { get; set; }

        [Option("branch", HelpText = "Name of repository to get repository merge policies for.")]
        public string Branch { get; set; }

        [Option("standard-automerge", HelpText = "Use standard auto-merge policies. GitHub ignores WIP and license/cla checks," +
            "Azure DevOps ignores comment, reviewer and work item linking. Neither will not auto-merge if changes are requested.")]
        public bool StandardAutoMergePolicies { get; set; }

        [Option("all-checks-passed", HelpText = "PR is automatically merged if there is at least one checks and all are passed. " +
            "Optionally provide a comma separated list of ignored check with --ignore-checks.")]
        public bool AllChecksSuccessfulMergePolicy { get; set; }

        [Option("ignore-checks", Separator = ',', HelpText = "For use with --all-checks-passed. A set of checks that are ignored.")]
        public IEnumerable<string> IgnoreChecks { get; set; }

        [Option("no-requested-changes", HelpText = "PR is not merged if there are changes requested or the PR has been rejected.")]
        public bool NoRequestedChangesMergePolicy { get; set; }

        [Option("no-extra-commits", HelpText = "PR is automatically merged if no non-bot commits exist in the PR.")]
        public bool NoExtraCommitsMergePolicy { get; set; }

        [Option('q', "quiet", HelpText = "Non-interactive mode (requires all elements to be passed on the command line).")]
        public bool Quiet { get; set; }

        public override Operation GetOperation()
        {
            return new SetRepositoryMergePoliciesOperation(this);
        }
    }
}
