// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

internal class CherryPickCommandLineOptions : VmrCommandLineOptions<CherryPickOperation>
{
    public override string Verb => "cherry-pick";

    public override string VerbDescription => "Cherry-picks a single commit from a repository to/from the VMR. " +
                                              "Must be called from the VMR directory or from the repository folder.";

    public string Source { get; set; } = string.Empty;

    public string Commit { get; set; } = string.Empty;

    protected override void Configure()
    {
        Add<string>("--source", "Path to the source repository folder", value => Source = value, required: true);
        Add<string>("--commit", "Commit SHA to cherry-pick", value => Commit = value, required: true);
        base.Configure();
    }

    protected override LogLevel DefaultLogVerbosity => LogLevel.Information;
}
