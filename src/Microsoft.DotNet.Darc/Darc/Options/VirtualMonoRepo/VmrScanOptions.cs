// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;

#nullable enable
namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

internal abstract class VmrScanOptions : VmrCommandLineOptions
{
    [Option("baseline-file", Required = false, HelpText = "Path to the scan baseline file (list of files ignored by the scan)")]
    public string? BaselineFilePath { get; set; } = null;
}
