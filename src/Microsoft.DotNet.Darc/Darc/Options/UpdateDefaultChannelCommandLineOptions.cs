// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

[Verb("update-default-channel", HelpText = "Update an existing default channel association.")]
internal class UpdateDefaultChannelCommandLineOptions : ConfigurationManagementCommandLineOptions<UpdateDefaultChannelOperation>
{
    [Option("id", Default = -1, HelpText = "Id of the default channel to update. Either --id or a combination of --repo, --branch, and --channel must be specified.")]
    public int Id { get; set; }

    [Option("repo", HelpText = "Repository of the default channel to update.")]
    public string Repository { get; set; }

    [Option("branch", HelpText = "Branch of the default channel to update.")]
    public string Branch { get; set; }

    [Option("channel", HelpText = "Channel of the default channel to update.")]
    public string Channel { get; set; }

    [Option("new-repo", HelpText = "New repository for the default channel.")]
    public string NewRepository { get; set; }

    [Option("new-branch", HelpText = "New branch for the default channel.")]
    public string NewBranch { get; set; }

    [Option("new-channel", HelpText = "New channel for the default channel.")]
    public string NewChannel { get; set; }

    [Option('e', "enable", HelpText = "Enable the default channel.")]
    public bool? Enable { get; set; }

    [Option('d', "disable", HelpText = "Disable the default channel.")]
    public bool? Disable { get; set; }
}
