// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using CommandLine;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

[Verb("backflow", HelpText = "Flows source code from the current commit of a locally checked out VMR " +
                             "into a target local repository. Must be called from the VMR directory." +
                             "Changes need to be committed.")]
internal class BackflowCommandLineOptions : CodeFlowCommandLineOptions<BackflowOperation>
{
    [Value(0, Required = true, HelpText = "Repository (mapping) name and the path to its local clone in the format name:path. " +
                                          @"Example: sdk:D:\repos\sdk")]
    public string Repository { get; set; }

    public override IEnumerable<string> Repositories => [Repository];
}
