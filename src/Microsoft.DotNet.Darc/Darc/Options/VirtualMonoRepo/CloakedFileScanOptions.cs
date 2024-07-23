// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;
using Microsoft.Extensions.DependencyInjection;

#nullable enable
namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

[Verb("scan-cloaked-files", HelpText = "Scans the VMR, checking if it contains any cloaked files")]
internal class CloakedFileScanOptions : VmrScanOptions
{
    public override Operation GetOperation(ServiceProvider sp) => ActivatorUtilities.CreateInstance<CloakedFileScanOperation>(sp, this);
}
