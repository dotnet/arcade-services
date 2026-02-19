// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

[Verb("login", HelpText = "Authenticate with Maestro and store credentials for use by automation tools.")]
internal class LoginCommandLineOptions : CommandLineOptions<LoginOperation>
{
    [Option("bar-uri", HelpText = "URI of the Build Asset Registry service to authenticate with. Defaults to production Maestro.")]
    public string BarUri { get; set; }
}
