// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

[Verb("merge-bands", HelpText = "Merges a band branch into the current branch in the VMR, excluding specific files.")]
internal class MergeBandsCommandLineOptions : VmrCommandLineOptionsBase<MergeBandsOperation>
{
    [Value(0, MetaName = "Source branch", Required = true, HelpText = "The branch name to merge from (e.g., release/10.0.1xx)")]
    public string SourceBranch { get; set; } = string.Empty;

    protected override LogLevel DefaultLogVerbosity => LogLevel.Information;
}
