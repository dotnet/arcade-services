// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using CommandLine;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

[Verb("backflow", HelpText = "Flows code changes from the VMR back to target repositories.")]
internal class BackflowCommandLineOptions : CodeFlowCommandLineOptions, IBaseVmrCommandLineOptions
{
    [Value(0, Required = true, HelpText = "Repositories to backflow in the form of NAME:PATH with mapping name and local path to the target repository. " +
        "Path can be ommitted when --repository-dirs is supplied. " +
        "When no repositories passed, all repositories with changes will be synchronized.")]
    public override IEnumerable<string> Repositories { get; set; }

    public override Operation GetOperation(ServiceProvider sp) => ActivatorUtilities.CreateInstance<BackflowOperation>(sp, this);
}
