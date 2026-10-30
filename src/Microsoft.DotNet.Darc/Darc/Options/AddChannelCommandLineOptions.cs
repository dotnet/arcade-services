// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

internal class AddChannelCommandLineOptions : ConfigurationManagementCommandLineOptions<AddChannelOperation>
{
    public override string Verb => "add-channel";

    public override string VerbDescription => "Creates a new channel.";

    public string Name { get; set; }

    public string Classification { get; set; }

    public bool Internal { get; set; }

    protected override void Configure()
    {
        Add<string>("--name", "-n", "Name of channel to create.", value => Name = value, required: true);
        Add<string>("--classification", "-c", "Classification of channel. Defaults to 'dev'.", value => Classification = value, defaultValue: () => "dev");
        Add<bool>("--internal", "-i", "Channel is internal only. This option is currently non-functional", value => Internal = value);
        base.Configure();
    }

    public override bool IsOutputFormatSupported()
        => OutputFormat switch
        {
            DarcOutputType.json => true,
            _ => base.IsOutputFormatSupported(),
        };
}
