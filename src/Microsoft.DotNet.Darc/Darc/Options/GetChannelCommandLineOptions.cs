// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

internal class GetChannelCommandLineOptions : CommandLineOptions<GetChannelOperation>
{
    public override string Verb => "get-channel";

    public override string VerbDescription => "Get a specific channel.";

    public int Id { get; set; }

    protected override void Configure()
    {
        Add<int>("--id", "ID of the channel to show.", value => Id = value, required: true);
        base.Configure();
    }

    public override bool IsOutputFormatSupported()
        => OutputFormat switch
        {
            DarcOutputType.json => true,
            _ => base.IsOutputFormatSupported(),
        };
}
