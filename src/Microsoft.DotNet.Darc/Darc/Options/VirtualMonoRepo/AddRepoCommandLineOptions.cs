// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using CommandLine;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

[Verb("add-repo", HelpText = "Adds new repo(s) to the VMR that haven't been synchronized yet.")]
internal class AddRepoCommandLineOptions : VmrCommandLineOptions<AddRepoOperation>, IBaseVmrCommandLineOptions
{
    [Value(0, Required = true, HelpText =
        "Repository names in the form of NAME or NAME:REVISION where REVISION is a commit SHA or other git reference (branch, tag). " +
        "Omitting REVISION will synchronize the repo to current HEAD.")]
    public IEnumerable<string> Repositories { get; set; }

    // Required by IBaseVmrCommandLineOptions but not used for this command
    public IEnumerable<string> AdditionalRemotes { get; set; } = [];
}
