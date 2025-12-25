// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DotNet.Darc.Options;

[Verb("delete-default-channel", HelpText = "Remove a default channel association.")]
internal class DeleteDefaultChannelCommandLineOptions : ConfigurationManagementCommandLineOptions<DeleteDefaultChannelOperation>, IUpdateDefaultChannelBaseCommandLineOptions
{
    [Option("id", Default = -1, HelpText = "Existing default channel id")]
    public int Id { get; set; }

    [Option("channel", HelpText = "Existing default channel association target channel name.")]
    public string Channel { get; set; }

    [Option("branch", HelpText = "Existing default channel association source branch name.")]
    public string Branch { get; set; }

    [Option("repo", HelpText = "Existing default channel association source repository name.")]
    public string Repository { get; set; }

    public override DeleteDefaultChannelOperation GetOperation(ServiceProvider sp)
    {
        return ActivatorUtilities.CreateInstance<DeleteDefaultChannelOperation>(sp, this);
    }
}
