// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

[Verb("default-channel-status", HelpText = "Enables or disables a default channel association")]
internal class DefaultChannelStatusCommandLineOptions : ConfigurationManagementCommandLineOptions<DefaultChannelStatusOperation>, IUpdateDefaultChannelBaseCommandLineOptions
{
    [Option("id", Default = -1, HelpText = "Existing default channel id")]
    public int Id { get; set; }

    [Option("channel", HelpText = "Existing default channel association target channel name.")]
    public string Channel { get; set; }

    [Option("branch", HelpText = "Existing default channel association source branch name.")]
    public string Branch { get; set; }

    [Option("repo", HelpText = "Existing default channel association source repository name.")]
    public string Repository { get; set; }

    [Option('e', "enable", HelpText = "Enable default channel.")]
    public bool Enable { get; set; }

    [Option('d', "disable", HelpText = "Disable default channel.")]
    public bool Disable { get; set; }
}
