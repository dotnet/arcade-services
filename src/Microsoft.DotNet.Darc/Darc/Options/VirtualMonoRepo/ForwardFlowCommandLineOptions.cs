// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using CommandLine;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

[Verb("forwardflow", HelpText = "Flows code changes from a target repository to the VMR.")]
internal class ForwardFlowCommandLineOptions : CodeFlowCommandLineOptions<ForwardFlowOperation>, IBaseVmrCommandLineOptions
{
    [Value(0, Required = false, HelpText = "Repositories to flow the code from in the form of NAME:PATH with mapping name and local path to the target repository. " +
        "Path can be ommitted when --repository-dirs is supplied. " +
        "When no repositories passed, all repositories with changes will be synchronized.")]
    public override IEnumerable<string> Repositories { get; set; }
}
