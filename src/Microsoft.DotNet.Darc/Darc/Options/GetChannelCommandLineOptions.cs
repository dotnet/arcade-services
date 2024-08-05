// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

[Verb("get-channel", HelpText = "Get a specific channel.")]
internal class GetChannelCommandLineOptions : CommandLineOptions<GetChannelOperation>
{
    [Option("id", Required = true, HelpText = "ID of the channel to show.")]
    public int Id { get; set; }

    public override bool IsOutputFormatSupported()
        => OutputFormat switch
        {
            DarcOutputType.json => true,
            _ => base.IsOutputFormatSupported(),
        };
}
