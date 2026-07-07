// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

[Verb("reset-submodule", HelpText =
    "Resets the contents of a VMR submodule to match the current HEAD of the submodule repository " +
    "you run this command from, and updates its record in source-manifest.json (stages the changes only).")]
internal class ResetSubmoduleCommandLineOptions : VmrCommandLineOptions<ResetSubmoduleOperation>
{
    [Value(0, MetaName = "Submodule path", Required = true, HelpText =
        "Path of the submodule to reset as it appears in source-manifest.json " +
        "(in the form [mapping]/[path within the mapping], e.g. runtime/src/external/foo). " +
        "The command must be run from a local clone of that submodule's repository; its current HEAD " +
        "is used as the target content and commit.")]
    public string Path { get; set; }
}
