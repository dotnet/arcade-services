// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DotNet.Darc.Options;

[Verb("get-goal", HelpText = "Gets Goal in minutes for a Definition in a Channel")]
internal class GetGoalCommandLineOptions : CommandLineOptions
{
    [Option('c', "channel", Required = true, HelpText = "Name of channel Eg : .Net Core 5 Dev ")]
    public string Channel { get; set; }

    [Option('d', "definition-id", Required = true, HelpText = "Azure DevOps Definition Id.")]
    public int DefinitionId { get; set; }

    public override Operation GetOperation(ServiceProvider sp) => ActivatorUtilities.CreateInstance<GetGoalOperation>(sp, this);

}
