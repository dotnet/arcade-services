// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

#nullable enable
namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

internal class VmrDiffOptions : VmrCommandLineOptions<VmrDiffOperation>
{
    public override string Verb => "diff";

    public override string VerbDescription => "Diffs the VMR and the product repositories. Outputs the diff to stdout, "
        + "or saves it to a patch file (or multiple if patch > 1 GB), if --output-path is provided";

    public string? OutputPath { get; set; }

    public string? Repositories { get; set; }

    public bool NameOnly { get; set; }

    public IEnumerable<string> Paths { get; set; } = [];

    protected override void Configure()
    {
        AddValue(
            "Repo specs",
            "Optional repository specification for diff calculation. Usage patterns:\n" +
            "1. No argument: Auto-detects based on current directory\n" +
            "   - When called from a VMR dir, diffs against all repositories in source-manifest.json.\n" +
            "   - When called from a product repo dir, diffs against VMR commit stored in the <Source> element.\n" +
            "2. Repository name: e.g. 'sdk' - When called from a VMR dir, diffs against the lastly synced commit of 'sdk' from source-manifest.json.\n" +
            "3. Single repository: 'remote:branch' - Diffs current directory against specified target. Remote can be local path or URI.\n" +
            "4. Two repositories: 'remote1:branch1..remote2:branch2' - Diffs the two specified repositories against each other. Remote can be local path or URI.",
            false,
            value => Repositories = value);
        Add<string>("--output-path", "Path where git patch(es) will be created (patches are split to be under 1GB)", value => OutputPath = value);
        Add<bool>("--name-only",
            "Only list names of differing files and directories. " +
            "Listed differences are prefixed with +, - or * for addition, removal or differing content.",
            value => NameOnly = value);
        AddList("--path",
            "Paths to include in the diff. When specified, only differences in these paths will be shown. " +
            "Can be specified multiple times to include multiple paths.",
            value => Paths = value);
        base.Configure();
    }
}
