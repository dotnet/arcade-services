// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;
using Microsoft.Extensions.DependencyInjection;

#nullable enable
namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

internal class GetRepoVersionCommandLineOptions : VmrCommandLineOptionsBase<GetRepoVersionOperation>
{
    public override string Verb => "get-version";

    public override string VerbDescription => "Gets the current version (a SHA) of a repository in the VMR.";

    public IEnumerable<string> Repositories { get; set; } = Array.Empty<string>();

    protected override void Configure()
    {
        AddValueList(
            "Repository names",
            "Repository names (e.g. runtime) to get the versions for.",
            false,
            value => Repositories = value);
        base.Configure();
    }

    public override IServiceCollection RegisterServices(IServiceCollection services)
    {
        RegisterVmrServices(services, tmpPath: null);
        return base.RegisterServices(services);
    }
}
