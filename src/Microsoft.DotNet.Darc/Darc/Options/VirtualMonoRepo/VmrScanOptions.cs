// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Darc.Operations;

#nullable enable
namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

internal interface IVmrScanOptions
{
    string? BaselineFilePath { get; set; }
}

internal abstract class VmrScanOptions<T> : VmrCommandLineOptions<T>, IVmrScanOptions where T : Operation
{
    public string? BaselineFilePath { get; set; } = null;

    protected override void Configure()
    {
        Add<string>("--baseline-file", "Path to the scan baseline file (list of files ignored by the scan)", value => BaselineFilePath = value);
        base.Configure();
    }
}
