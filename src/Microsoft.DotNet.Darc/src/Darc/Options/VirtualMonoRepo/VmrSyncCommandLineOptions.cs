// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using CommandLine;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

internal abstract class VmrSyncCommandLineOptions : VmrCommandLineOptions
{
    [Value(0, Required = true, HelpText =
        "Repository names in the form of NAME or NAME:REVISION where REVISION is a commit SHA or other git reference (branch, tag). " +
        "Omitting REVISION will synchronize the repo to current HEAD. Pass 'all' to update all repositories.")]
    public IEnumerable<string> Repositories { get; set; }
}
