// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DotNet.Darc.Options;

[Verb("get-health", HelpText = "Evaluate health")]
internal class GetHealthCommandLineOptions : CommandLineOptions
{
    [Option("repo", HelpText = "Narrow health checkups by this repository.")]
    public string Repo { get; set; }

    [Option("channel", HelpText = "Narrow health checkups by this channel.")]
    public string Channel { get; set; }

    public override Operation GetOperation(ServiceProvider sp) => ActivatorUtilities.CreateInstance<GetHealthOperation>(sp, this);

}
