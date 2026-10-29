// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

internal class ResolveConflictCommandLineOptions : CodeFlowCommandLineOptions<ResolveConflictOperation>
{
    public override string Verb => "resolve-conflict";

    public override string VerbDescription => "Resolves a pending codeflow PR conflict locally.";

    public new string SubscriptionId { get; set; }

    public string SourceRepoPath { get; set; } = null;

    public override IEnumerable<string> Repositories => [SubscriptionId]; // We don't really need this field

    protected override void Configure()
    {
        Add<string>("--subscription", "-s", "Subscription for which to resolve the pending conflict", value => SubscriptionId = value, required: true);
        Add<string>("--source-repo", "Optional path to a local clone of the repo the change is flowing from. If not provided, source repo will be cloned into $DARC_TMP_DIR or $TEMP/$TMPDIR", value => SourceRepoPath = value);
        base.Configure();
    }
}
