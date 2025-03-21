// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

[Verb("get-asset", HelpText = "Get information about an asset.")]
internal class GetAssetCommandLineOptions : CommandLineOptions<GetAssetOperation>
{
    [Option("name", Required = false, HelpText = "Name of asset to look up")]
    public string Name { get; set; }

    [Option("version", HelpText = "Look up specific version of an asset.")]
    public string Version { get; set; }

    [Option("latest", HelpText = "Look up the latest version of an asset.")]
    public bool Latest { get; set; }

    [Option("channel", HelpText = "Look up the asset produced from builds applied to this channel.")]
    public string Channel { get; set; }

    [Option("build", HelpText = "If specified, scopes the search to a specific BAR build ID")]
    public int? Build { get; set; }

    [Option("max-age", Default = 30, HelpText = "Show builds with a max age of this many days.")]
    public int MaxAgeInDays { get; set; }

    public override bool IsOutputFormatSupported()
        => OutputFormat switch
        {
            DarcOutputType.json => true,
            _ => base.IsOutputFormatSupported(),
        };
}
