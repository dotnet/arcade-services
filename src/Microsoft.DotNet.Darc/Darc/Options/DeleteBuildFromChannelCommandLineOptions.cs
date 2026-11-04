// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

internal class DeleteBuildFromChannelCommandLineOptions : CommandLineOptions<DeleteBuildFromChannelOperation>
{
    public override string Verb => "delete-build-from-channel";

    public override string VerbDescription => "Removes a build from a channel.";

    [RedactFromLogging]
    public int Id { get; set; }

    public string Channel { get; set; }

    protected override void Configure()
    {
        Add<int>("--id", "BAR id of build to assign to channel.", value => Id = value, required: true);
        Add<string>("--channel", "Channel to remove the build from.", value => Channel = value, required: true);
        base.Configure();
    }
}
