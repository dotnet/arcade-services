// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.IO;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

internal class BackflowCommandLineOptions : CodeFlowCommandLineOptions<BackflowOperation>
{
    public override string Verb => "backflow";

    public override string VerbDescription => "Flows source code from the current commit of a locally checked out VMR " +
                                              "into a target local repository. Must be called from the VMR directory.";

    public string Repository { get; set; }

    public override IEnumerable<string> Repositories => [Path.GetFileName(Repository) + ":" + Repository];

    protected override void Configure()
    {
        AddValue(
            "Target repo path",
            "Path to the local repository on disk to flow the current VMR commit to",
            true,
            value => Repository = value);
        base.Configure();
    }
}
