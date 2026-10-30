// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

internal class GetHealthCommandLineOptions : CommandLineOptions<GetHealthOperation>
{
    public override string Verb => "get-health";

    public override string VerbDescription => "Evaluate health";

    public string Repo { get; set; }

    public string Channel { get; set; }

    protected override void Configure()
    {
        Add<string>("--repo", "Narrow health checkups by this repository.", value => Repo = value);
        Add<string>("--channel", "Narrow health checkups by this channel.", value => Channel = value);
        base.Configure();
    }
}
