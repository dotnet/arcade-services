// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

internal class VmrPushCommandLineOptions : VmrCommandLineOptions<PushOperation>
{
    public override string Verb => "push";

    public override string VerbDescription => "Pushes given VMR branch to a given remote. Verifies public availability of pushed commits.";

    public string RemoteUrl { get; set; }

    public string Branch { get; set; }

    public bool SkipCommitVerification { get; set; }

    public string CommitVerificationPat { get; set; }

    protected override void Configure()
    {
        Add<string>("--remote-url", "URL to push to", value => RemoteUrl = value, required: true);
        Add<string>("--branch", "Branch to push", value => Branch = value, required: true);
        Add<bool>("--skip-commit-verification", "Don't verify that each commit in the VMR can be found in the corresponding public repository on GitHub before pushing.", value => SkipCommitVerification = value);
        Add<string>("--commit-verification-pat", "Token for authenticating to GitHub GraphQL API. Needs to have only basic scope as it will be used to look for commits in public GitHub repos.", value => CommitVerificationPat = value);
        base.Configure();
    }
}
