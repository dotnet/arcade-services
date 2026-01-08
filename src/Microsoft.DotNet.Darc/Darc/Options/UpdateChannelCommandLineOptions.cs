// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

[Verb("update-channel", HelpText = "Update an existing channel's metadata.")]
internal class UpdateChannelCommandLineOptions : ConfigurationManagementCommandLineOptions<UpdateChannelOperation>
{
    [Option('i', "id", Required = true, HelpText = "Channel ID.")]
    public int Id { get; set; }

    [Option('n', "name", HelpText = "New name of channel.")]
    public string Name { get; set; }

    [Option('c', "classification", HelpText = "New classification of channel.")]
    public string Classification { get; set; }
}
