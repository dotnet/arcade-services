// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CommandLine.Text;
using CommandLine;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;
using System;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

[Verb("push", HelpText = "Pushes changes to the vmr.")]
internal class VmrPushCommandLineOptions : CommandLineOptions
{
    [Option("vmr", Required = true, HelpText = "Path to the VMR; defaults to nearest git root above the current working directory.")]
    public string VmrPath { get; set; } = Environment.CurrentDirectory;

    public override Operation GetOperation() => new PushOperation(this);
}
