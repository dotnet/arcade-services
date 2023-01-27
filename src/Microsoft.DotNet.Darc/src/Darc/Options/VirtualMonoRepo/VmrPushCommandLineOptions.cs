// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

[Verb("push", HelpText = "Pushes given VMR branch to a given remote. Optionally verifies public availability of pushed commits.")]
internal class VmrPushCommandLineOptions : VmrCommandLineOptions
{
    [Option("remote-url", Required = true, HelpText = "URL to push to")]
    public string RemoteUrl { get; set; }

    [Option("branch", Required = true, HelpText = "Branch to push")]
    public string Branch { get; set; }

    [Option("verify-commits", Required = false, HelpText = "If set, the tooling will verify that each commit in the VMR can be found in the corresponding public repository on GitHub before pushing. In this case a GitHub token must be specified.")]
    public bool VerifyCommits { get; set; }

    [Option("commit-verification-pat", Required = false, HelpText = "Token for authenticating to GitHub GraphQL API. Needs to have only basic scope as it will be used to look for commits in public GitHub repos.")]
    public string CommitVerificationPat { get; set; }

    public override Operation GetOperation() => new PushOperation(this);
}
