// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

[Verb("generate-tpn", HelpText = $"Generates a new {VmrInfo.ThirdPartyNoticesFileName}.")]
internal class GenerateTpnCommandLineOptions : VmrCommandLineOptions
{
    [Option("tpn-template", Required = true, HelpText = "Path to a header template for generating THIRD-PARTY-NOTICES file.")]
    public string TpnTemplate { get; set; }

    public override Operation GetOperation() => new GenerateTpnOperation(this);
}
