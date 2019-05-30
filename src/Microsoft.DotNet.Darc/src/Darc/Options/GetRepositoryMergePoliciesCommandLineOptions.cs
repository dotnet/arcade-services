// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options
{
    [Verb("get-repository-policies", HelpText = "Retrieves information about repository merge policies.")]
    internal class GetRepositoryMergePoliciesCommandLineOptions : CommandLineOptions
    {
        [Option("repo", HelpText = "Name of repository to get repository merge policies for. Match on substring")]
        public string Repo { get; set; }

        [Option("branch", HelpText = "Name of repository to get repository merge policies for. Match on substring")]
        public string Branch { get; set; }

        [Option("all", HelpText = "List all repositories. Otherwise, branches not targeted by a batchable subscription are not listed.")]
        public bool All { get; set; }

        public override Operation GetOperation()
        {
            return new GetRepositoryMergePoliciesOperation(this);
        }
    }
}
