// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

[Verb("cherry-pick", HelpText = "Cherry-picks a single commit from a repository to/from the VMR. " +
                                "Must be called from the VMR directory or from the repository folder.")]
internal class CherryPickCommandLineOptions : VmrCommandLineOptions<CherryPickOperation>
{
    [Option("source", Required = true, HelpText = "Path to the source repository folder")]
    public string Source { get; set; } = string.Empty;

    [Option("commit", Required = true, HelpText = "Commit SHA to cherry-pick")]
    public string Commit { get; set; } = string.Empty;

    protected override LogLevel DefaultLogVerbosity => LogLevel.Information;
}
