// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

internal interface IUpdateDefaultChannelBaseCommandLineOptions : ICommandLineOptions
{
    string Branch { get; set; }
    string Channel { get; set; }
    int Id { get; set; }
    string Repository { get; set; }
}

internal abstract class UpdateDefaultChannelBaseCommandLineOptions<T> : ConfigurationManagementCommandLineOptions<T>, IUpdateDefaultChannelBaseCommandLineOptions where T : Operation
{
    [Option("id", Default = -1, HelpText = "Existing default channel id")]
    public int Id { get; set; }

    [Option("channel", HelpText = "Existing default channel association target channel name.")]
    public string Channel { get; set; }

    [Option("branch", HelpText = "Existing default channel association source branch name.")]
    public string Branch { get; set; }

    [Option("repo", HelpText = "Existing default channel association source repository name.")]
    public string Repository { get; set; }
}
