// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using CommandLine;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;
using Microsoft.Extensions.DependencyInjection;

#nullable enable
namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

[Verb("get-version", HelpText = "Gets the current version (a SHA) of a repository in the VMR.")]
internal class GetRepoVersionCommandLineOptions : VmrCommandLineOptionsBase<GetRepoVersionOperation>
{
    [Value(0, MetaName = "Repository names", HelpText = "Repository names (e.g. runtime) to get the versions for.")]
    public IEnumerable<string> Repositories { get; set; } = Array.Empty<string>();

    public override IServiceCollection RegisterServices(IServiceCollection services)
    {
        RegisterVmrServices(services, tmpPath: null);
        return base.RegisterServices(services);
    }
}
