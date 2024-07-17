// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

[Verb("get-default-channels", HelpText = "Gets a list of repo+branch combinations and their associated default channels for builds.")]
internal class GetDefaultChannelsCommandLineOptions : CommandLineOptions
{
    [Option("source-repo", HelpText = "Filter by a specific source repository. Matches on substring.")]
    public string SourceRepository { get; set; }

    [Option("branch", HelpText = "Filter by a branch. Matches on substring.")]
    public string Branch { get; set; }

    [Option("channel", HelpText = "Filter by a channel name. Matches on substring.")]
    public string Channel { get; set; }

    public override Type GetOperation()
    {
        return typeof(GetDefaultChannelsOperation);
    }
}
