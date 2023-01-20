// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

[Verb("push", HelpText = "Pushes given VMR branch to a given remote. Optionally verifies commit public availability.")]
internal class VmrPushCommandLineOptions : VmrCommandLineOptions
{
    [Option("remote", Required = true, HelpText = "Name of an already existing remote to push to")]
    public string Remote { get; set; }

    [Option("branch", Required = true, HelpText = "Branch to push")]
    public string Branch { get; set; }

    [Option("github-api-pat", Required = true, HelpText = "Token used to authenticate for querying GitHub api")]
    public string GitHubApiPat { get; set; }

    public override Operation GetOperation() => new PushOperation(this);
}
