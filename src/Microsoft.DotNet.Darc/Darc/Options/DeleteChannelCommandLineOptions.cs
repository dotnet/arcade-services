// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

internal class DeleteChannelCommandLineOptions : ConfigurationManagementCommandLineOptions<DeleteChannelOperation>
{
    public override string Verb => "delete-channel";

    public override string VerbDescription => "Deletes an existing channel.";

    public string Name { get; set; }

    protected override void Configure()
    {
        Add<string>("--name", "-n", "Name of channel to delete.", value => Name = value, required: true);
        base.Configure();
    }
}
