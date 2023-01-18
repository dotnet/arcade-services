// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class CloakedFileScanOperation : ScanOperationBase<VmrCloakedFileScanner>
{
    public CloakedFileScanOperation(VmrScanOptions options) : base(options, options.RegisterServices())
    {
    }
}
