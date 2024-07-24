// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using CommandLine;
using Microsoft.DotNet.Darc.Operations;

#nullable enable
namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

internal abstract class VmrCommandLineOptionsBase<T> : CommandLineOptions<T> where T : Operation
{
    [Option("vmr", HelpText = "Path to the VMR; defaults to nearest git root above the current working directory.")]
    public string VmrPath { get; set; } = Environment.CurrentDirectory;
}
