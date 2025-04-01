﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
[Verb("diff", HelpText = "Diffs the VMR and the product repositories.")]
internal class VmrDiffOptions : VmrCommandLineOptions<VmrDiffOperation>
{
    [Option("output-path", HelpText = "Path where git patch(es) will be created (patches are split to be under 1GB)")]
    public string OutputPath { get; set; }

    [Value(0, Required = true, HelpText =
        "Repositories and git refs to calculate the diff for in the following format: repository:branch..repository:branch, " +
        "where repository can be a local path or remote URI. Alternatively, only one target can be provided in " +
        "which case current directory will be used as the source for the diff")]
    public string Repositories { get; set; }
}
