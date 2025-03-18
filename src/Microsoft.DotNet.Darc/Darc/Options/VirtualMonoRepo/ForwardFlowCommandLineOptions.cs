// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using CommandLine;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

[Verb("forwardflow", HelpText = "Flows source code from the current commit of a local repository into a local VMR. " +
                                "Must be called from the local repository folder. " +
                                "Changes need to be committed.")]
internal class ForwardFlowCommandLineOptions : CodeFlowCommandLineOptions<ForwardFlowOperation>
{
    public override IEnumerable<string> Repositories => [ "to VMR:" + Environment.CurrentDirectory ];
}
