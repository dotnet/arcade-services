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
        "Repositories and branches that the diff will be calculated for in the following format: repository:branch..repository:branch, " +
        "where repository can be a path to the local repo, or URI to a remote repo. If the command is run from a repository, then repository:branch " +
        "also works as an input. In this case, the diff will be between the current repo, and the one from the input")]
    public string Repositories { get; set; }
}
