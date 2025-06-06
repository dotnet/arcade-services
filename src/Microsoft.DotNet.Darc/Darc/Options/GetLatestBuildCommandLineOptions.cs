// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

[Verb("get-latest-build", HelpText = "Retrieves the latest builds matching the specified criteria. " +
                                     "If more than one build matches then multiple builds are returned.")]
internal class GetLatestBuildCommandLineOptions : CommandLineOptions<GetLatestBuildOperation>
{
    [Option("repo", Required = true, HelpText = "Name of repository to determine the latest build for. Match on substring")]
    public string Repo { get; set; }

    [Option("channel", HelpText = "Name of channel to query for the latest build on. Match on substring")]
    public string Channel { get; set; }

    public override bool IsOutputFormatSupported()
        => OutputFormat switch
        {
            DarcOutputType.json => true,
            _ => base.IsOutputFormatSupported(),
        };
}
