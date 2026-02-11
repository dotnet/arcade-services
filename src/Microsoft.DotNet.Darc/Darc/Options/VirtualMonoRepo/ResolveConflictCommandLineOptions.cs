// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using CommandLine;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

[Verb("resolve-conflict", HelpText = "Resolves a pending codeflow PR conflict locally.")]
internal class ResolveConflictCommandLineOptions : CodeFlowCommandLineOptions<ResolveConflictOperation>
{
    [Option('s', "subscription", Required = true, HelpText = "Subscription for which to resolve the pending conflict")]
    public new string SubscriptionId { get; set; }

    [Option("source-repo", Required = false, HelpText = "Optional path to a local clone of the repo the change is flowing from. If not provided, source repo will be cloned into $DARC_TMP_DIR or $TEMP/$TMPDIR")]
    public string SourceRepoPath { get; set; } = null;

    public override IEnumerable<string> Repositories => [SubscriptionId]; // We don't really need this field
}
