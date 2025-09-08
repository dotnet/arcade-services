// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

[Verb("get-dependencies", HelpText = "Get local dependencies.")]
internal class GetDependenciesCommandLineOptions : CommandLineOptions<GetDependenciesOperation>
{
    [Option('n', "name", HelpText = "Name of dependency to query for.")]
    public string Name { get; set; }

    [Option("relative-base-path", HelpText = "Used for VMR repos. Relative base path of the repo the dependency is being added to (e.g. src/sdk)", Required = false)]
    public string RelativeBasePath { get; set; } = null;
}
