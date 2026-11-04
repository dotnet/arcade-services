// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

internal class GenerateTpnCommandLineOptions : VmrCommandLineOptions<GenerateTpnOperation>
{
    public override string Verb => "generate-tpn";

    public override string VerbDescription => $"Generates a new {VmrInfo.ThirdPartyNoticesFileName}.";

    public string TpnTemplate { get; set; }

    protected override void Configure()
    {
        Add<string>("--tpn-template", "Path to a header template for generating THIRD-PARTY-NOTICES file.", value => TpnTemplate = value, required: true);
        base.Configure();
    }
}
