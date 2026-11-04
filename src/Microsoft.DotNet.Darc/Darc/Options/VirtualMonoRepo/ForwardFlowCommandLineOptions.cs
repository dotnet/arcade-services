// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

internal class ForwardFlowCommandLineOptions : CodeFlowCommandLineOptions<ForwardFlowOperation>
{
    public override string Verb => "forwardflow";

    public override string VerbDescription => "Flows source code from the current commit of a local repository into a local VMR. " +
                                              "Must be called from the local repository folder or with the `subscription` parameter";

    // This argument would not be necessary as we have the --vmr option but just to keep the forward and backflow commands
    // follow the consistent format `darc vmr *flow [target]`, where the target can be either a repository or a VMR.
    public string Vmr { get; set; }

    public override IEnumerable<string> Repositories => [ "to VMR:" + Environment.CurrentDirectory ];

    protected override void Configure()
    {
        AddValue(
            "VMR path",
            "Path to the VMR on disk to flow the current commit to. Can be used instead of the --vmr option.",
            false,
            value => Vmr = value);
        base.Configure();
    }

    public override IServiceCollection RegisterServices(IServiceCollection services)
    {
        if (!string.IsNullOrEmpty(Vmr))
        {
            VmrPath = Vmr;
        }

        return base.RegisterServices(services);
    }
}
