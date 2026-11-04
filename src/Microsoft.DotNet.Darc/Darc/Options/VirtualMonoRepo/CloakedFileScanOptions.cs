// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

#nullable enable
namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

internal class CloakedFileScanOptions : VmrScanOptions<CloakedFileScanOperation>
{
    public override string Verb => "scan-cloaked-files";

    public override string VerbDescription => "Scans the VMR, checking if it contains any cloaked files";
}
