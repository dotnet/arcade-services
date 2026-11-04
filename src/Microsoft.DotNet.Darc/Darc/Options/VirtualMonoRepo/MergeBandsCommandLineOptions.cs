// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

internal class MergeBandsCommandLineOptions : VmrCommandLineOptionsBase<MergeBandsOperation>
{
    public override string Verb => "merge-bands";

    public override string VerbDescription => "Merges a band branch into the current branch in the VMR, excluding specific files.";

    public string SourceBranch { get; set; } = string.Empty;

    protected override LogLevel DefaultLogVerbosity => LogLevel.Information;

    protected override void Configure()
    {
        AddValue(
            "Source branch",
            "The branch name to merge from (e.g., release/10.0.1xx)",
            true,
            value => SourceBranch = value);
        base.Configure();
    }
}
