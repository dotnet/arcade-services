// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CommandLine;

#nullable enable
namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

internal abstract class VmrScanOptions : VmrCommandLineOptions
{
    [Option("baseline-file", Required = true, HelpText = "Path to the VMR baseline file")]
    public string BaselineFilePath { get; set; } = string.Empty;
}
