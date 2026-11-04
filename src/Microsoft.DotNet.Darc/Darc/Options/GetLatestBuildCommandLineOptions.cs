// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

internal class GetLatestBuildCommandLineOptions : CommandLineOptions<GetLatestBuildOperation>
{
    public override string Verb => "get-latest-build";

    public override string VerbDescription => "Retrieves the latest builds matching the specified criteria. " +
                                              "If more than one build matches then multiple builds are returned.";

    public string Repo { get; set; }

    public string Channel { get; set; }

    protected override void Configure()
    {
        Add<string>("--repo", "Name of repository to determine the latest build for. Match on substring", value => Repo = value, required: true);
        Add<string>("--channel", "Name of channel to query for the latest build on. Match on substring", value => Channel = value);
        base.Configure();
    }

    public override bool IsOutputFormatSupported()
        => OutputFormat switch
        {
            DarcOutputType.json => true,
            _ => base.IsOutputFormatSupported(),
        };
}
