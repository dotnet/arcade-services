// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

internal class DefaultChannelStatusCommandLineOptions : ConfigurationManagementCommandLineOptions<DefaultChannelStatusOperation>, IUpdateDefaultChannelBaseCommandLineOptions
{
    public override string Verb => "default-channel-status";

    public override string VerbDescription => "Enables or disables a default channel association";

    public int Id { get; set; }

    public string Channel { get; set; }

    public string Branch { get; set; }

    public string Repository { get; set; }

    public bool Enable { get; set; }

    public bool Disable { get; set; }

    protected override void Configure()
    {
        Add<int>("--id", "Existing default channel id", value => Id = value, defaultValue: () => -1);
        Add<string>("--channel", "Existing default channel association target channel name.", value => Channel = value);
        Add<string>("--branch", "Existing default channel association source branch name.", value => Branch = value);
        Add<string>("--repo", "Existing default channel association source repository name.", value => Repository = value);
        Add<bool>("--enable", "-e", "Enable default channel.", value => Enable = value);
        Add<bool>("--disable", "-d", "Disable default channel.", value => Disable = value);
        base.Configure();
    }
}
