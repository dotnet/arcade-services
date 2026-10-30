// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

internal class UpdateChannelCommandLineOptions : ConfigurationManagementCommandLineOptions<UpdateChannelOperation>
{
    public override string Verb => "update-channel";

    public override string VerbDescription => "Update an existing channel's metadata.";

    public int Id { get; set; }

    public string Name { get; set; }

    public string Classification { get; set; }

    protected override void Configure()
    {
        Add<int>("--id", "-i", "Channel ID.", value => Id = value, required: true);
        Add<string>("--name", "-n", "New name of channel.", value => Name = value);
        Add<string>("--classification", "-c", "New classification of channel.", value => Classification = value);
        base.Configure();
    }
}
