// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

internal class GetRepositoryMergePoliciesCommandLineOptions : CommandLineOptions<GetRepositoryMergePoliciesOperation>
{
    public override string Verb => "get-repository-policies";

    public override string VerbDescription => "Retrieves information about repository merge policies.";

    public string Repo { get; set; }

    public string Branch { get; set; }

    public bool All { get; set; }

    protected override void Configure()
    {
        Add<string>("--repo", "Name of repository to get repository merge policies for. Match on substring", value => Repo = value);
        Add<string>("--branch", "Name of repository to get repository merge policies for. Match on substring", value => Branch = value);
        Add<bool>("--all", "List all repositories. Otherwise, branches not targeted by a batchable subscription are not listed.", value => All = value);
        base.Configure();
    }
}
