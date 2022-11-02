// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

[Verb("generate-tpn", HelpText = $"Generates a new {VmrInfo.ThirdPartyNoticesFileName}.")]
internal class GenerateTpnCommandLineOptions : VmrCommandLineOptions
{
    public override Operation GetOperation() => new GenerateTpnOperation(this);
}
