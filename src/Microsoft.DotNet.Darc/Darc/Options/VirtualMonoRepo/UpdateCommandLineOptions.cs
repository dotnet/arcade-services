// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

[Verb("update", HelpText = "Updates given repo(s) in the VMR to match given refs.")]
internal class UpdateCommandLineOptions : VmrSyncCommandLineOptions<UpdateOperation>
{
    [Option('r', "recursive", Required = false, HelpText = $"Process also dependencies (from {VersionFiles.VersionDetailsXml}) recursively.")]
    public bool Recursive { get; set; } = false;
}
