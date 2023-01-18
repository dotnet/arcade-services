// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CommandLine;

#nullable enable
namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

internal abstract class VmrScanOptions : VmrCommandLineOptions
{
    [Option("baseline-file", Required = true, HelpText = "Path to the file containing a list of the VMR baseline files")]
    public string BaselineFilesPath { get; set; } = string.Empty;
}
