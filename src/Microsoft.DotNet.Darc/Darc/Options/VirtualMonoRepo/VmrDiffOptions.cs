// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
[Verb("diff", HelpText = "Diff the contents of the virtual repo with the contents of the source repos.")]
internal class VmrDiffOptions : VmrCommandLineOptions<VmrDiffOperation>
{
    [Option("output-path", HelpText = "Path where the git patch will be created")]
    public string OutputPath { get; set; }

    [Value(0, Required = true, HelpText =
        "Repository names in the form of NAME or NAME:REVISION where REVISION is a commit SHA or other git reference (branch, tag). " +
        "Omitting REVISION will synchronize the repo to current HEAD.")]
    public string Input { get; set; }
}
