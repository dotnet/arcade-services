// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

internal class SetRepositoryMergePoliciesCommandLineOptions : ConfigurationManagementCommandLineOptions<SetRepositoryMergePoliciesOperation>
{
    public override string Verb => "set-repository-policies";

    public override string VerbDescription => "Set Merge PRs options for the specific repository and branch";

    public string Repository { get; set; }

    public string Branch { get; set; }

    public bool? MergePrs { get; set; }

    public IReadOnlyCollection<string> IgnoredChecks { get; set; } = [];

    public bool Quiet { get; set; }

    protected override void Configure()
    {
        Add<string>("--repo", "Name of repository to set repository merge policies for.", value => Repository = value);
        Add<string>("--branch", "Name of repository to get repository merge policies for.", value => Branch = value);
        Add<bool?>("--merge-prs", "Whether Maestro should merge pull requests after all Maestro checks pass.", value => MergePrs = value);
        AddSeparatedList(',', "--ignore-checks", "A comma-separated list of checks ignored when evaluating pull requests.", value => IgnoredChecks = value);
        Add<bool>("--quiet", "-q", "Non-interactive mode (requires all elements to be passed on the command line).", value => Quiet = value);
        base.Configure();
    }
}
