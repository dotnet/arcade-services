// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

internal class GetAssetCommandLineOptions : CommandLineOptions<GetAssetOperation>
{
    public override string Verb => "get-asset";

    public override string VerbDescription => "Get information about an asset.";

    public string Name { get; set; }

    public string Version { get; set; }

    public bool Latest { get; set; }

    public string Channel { get; set; }

    public int? Build { get; set; }

    public int MaxAgeInDays { get; set; }

    protected override void Configure()
    {
        Add<string>("--name", "Name of asset to look up", value => Name = value);
        Add<string>("--version", "Look up specific version of an asset.", value => Version = value);
        Add<bool>("--latest", "Look up the latest version of an asset.", value => Latest = value);
        Add<string>("--channel", "Look up the asset produced from builds applied to this channel.", value => Channel = value);
        Add<int?>("--build", "If specified, scopes the search to a specific BAR build ID", value => Build = value);
        Add<int>("--max-age", "Show builds with a max age of this many days.", value => MaxAgeInDays = value, defaultValue: () => 30);
        base.Configure();
    }

    public override bool IsOutputFormatSupported()
        => OutputFormat switch
        {
            DarcOutputType.json => true,
            _ => base.IsOutputFormatSupported(),
        };
}
