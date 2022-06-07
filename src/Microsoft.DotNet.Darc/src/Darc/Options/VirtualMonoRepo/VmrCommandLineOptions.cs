// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using CommandLine;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

internal abstract class VmrCommandLineOptions : CommandLineOptions
{
    [Value(0, Required = true, HelpText = "Repository names in the form of NAME or NAME:REVISION where REVISION is a specific commit SHA. Omitting REVISION will synchronize the repo to current HEAD. Pass 'all' to update all repositories.")]
    public IEnumerable<string> Repositories { get; set; }

    [Option("vmr", Required = false, HelpText = "Path to the virtual mono repo; defaults to cwd.")]
    public string VmrPath { get; set; } = Environment.CurrentDirectory;

    [Option("tmp", Required = true, HelpText = "Temporary path where intermediate files are stored and repositories are cloned to.")]
    public string TmpPath { get; set; }
}
