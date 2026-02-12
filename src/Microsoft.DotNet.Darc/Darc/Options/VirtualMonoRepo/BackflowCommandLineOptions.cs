// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.IO;
using CommandLine;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

[Verb("backflow", HelpText = "Flows source code from the current commit of a locally checked out VMR " +
                             "into a target local repository. Must be called from the VMR directory.")]
internal class BackflowCommandLineOptions : CodeFlowCommandLineOptions<BackflowOperation>
{
    [Value(0,
        Required = true,
        MetaName = "Repo path",
        HelpText = "Path to the local repository on disk to flow the current VMR commit to")]
    public string Repository { get; set; }

    public override IEnumerable<string> Repositories => [Path.GetFileName(Repository) + ":" + Repository];
}
