// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

[Verb("set-goal", HelpText = "Creates/Updates Goal in minutes for a Definition in a Channel")]
internal class SetGoalCommandLineOptions : CommandLineOptions<SetGoalOperation>
{
    [Option('c', "channel", Required = true, HelpText = "Name of channel Eg : .Net Core 5 Dev ")]
    public string Channel { get; set; }

    [Option('d', "definition-id", Required = true, HelpText = "Azure DevOps Definition Id.")]
    public int DefinitionId { get; set; }

    [Option('m', "minutes", Required = true, HelpText = "Goal time in minutes.")]
    public int Minutes { get; set; }
}
