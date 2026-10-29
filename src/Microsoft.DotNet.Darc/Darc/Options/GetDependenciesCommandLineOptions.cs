// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

internal class GetDependenciesCommandLineOptions : CommandLineOptions<GetDependenciesOperation>
{
    public override string Verb => "get-dependencies";

    public override string VerbDescription => "Get local dependencies.";

    public string Name { get; set; }

    public string RelativeBasePath { get; set; } = null;

    protected override void Configure()
    {
        Add<string>("--name", "-n", "Name of dependency to query for.", value => Name = value);
        Add<string>("--relative-base-path", "Relative base path where dependencies will be read from (e.g. src/sdk would read it from src/sdk/eng/Version.Details.xml). Commonly used with the VMR", value => RelativeBasePath = value);
        base.Configure();
    }
}
