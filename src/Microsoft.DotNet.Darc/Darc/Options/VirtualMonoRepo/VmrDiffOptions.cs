// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

#nullable enable
namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

[Verb("diff", HelpText = "Diffs the VMR and the product repositories. Outputs the diff to stdout, "
    + "or saves it to a patch file (or multiple if patch > 1 GB), if --output-path is provided")]
internal class VmrDiffOptions : VmrCommandLineOptions<VmrDiffOperation>
{
    [Option("output-path", HelpText = "Path where git patch(es) will be created (patches are split to be under 1GB)", Required = false)]
    public string? OutputPath { get; set; }

    [Value(0, MetaName = "Repo specs", Required = false, HelpText =
        "Optional repository specification for diff calculation. Usage patterns:\n" +
        "1. No argument: Auto-detects based on current directory\n" +
        "   - When called from a VMR dir, diffs against all repositories in source-manifest.json.\n" +
        "   - When called from a product repo dir, diffs against VMR commit stored in the <Source> element.\n" +
        "2. Repository name: e.g. 'sdk' - When called from a VMR dir, diffs against the lastly synced commit of 'sdk' from source-manifest.json.\n" +
        "3. Single repository: 'remote:branch' - Diffs current directory against specified target. Remote can be local path or URI.\n" +
        "4. Two repositories: 'remote1:branch1..remote2:branch2' - Diffs the two specified repositories against each other. Remote can be local path or URI.")]
    public string? Repositories { get; set; }

    [Option("name-only", Required = false, HelpText =
        "Only list names of differing files and directories. " +
        "Listed differences are prefixed with +, - or * for addition, removal or differing content.")]
    public bool NameOnly { get; set; }
}
