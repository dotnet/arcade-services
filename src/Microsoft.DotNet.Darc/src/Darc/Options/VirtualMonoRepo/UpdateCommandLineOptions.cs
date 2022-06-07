// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

[Verb("update", HelpText = "Synchronizes local files of given individual repo(s) to match given revisions.")]
internal class UpdateCommandLineOptions : VmrCommandLineOptions
{
    [Option("no-squash", HelpText = "Synchronizes commit by commit instead of squashing all updates into one.")]
    public bool NoSquash { get; set; } = false;

    [Option("ignore-working-tree", HelpText = "Does not keep working tree clean after commits for faster synchronization (changes are applied into the index directly).")]
    public bool IgnoreWorkingTree { get; set; } = false;

    public override Operation GetOperation() => new UpdateOperation(this);
}
