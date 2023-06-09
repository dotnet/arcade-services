// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using System;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class BinaryFileScanOperation : ScanOperationBase<VmrBinaryFileScanner>
{
    public BinaryFileScanOperation(BinaryFileScanOptions options) : base(options)
    {
    }
}
