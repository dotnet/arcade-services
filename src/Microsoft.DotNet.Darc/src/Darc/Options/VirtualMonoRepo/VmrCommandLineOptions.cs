// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using CommandLine;
using Microsoft.DotNet.DarcLib;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

internal abstract class VmrCommandLineOptions : CommandLineOptions
{
    [Value(0, Required = true, HelpText = 
        "Repository names in the form of NAME or NAME:REVISION where REVISION is a commit SHA or other git reference (branch, tag). " +
        "Omitting REVISION will synchronize the repo to current HEAD. Pass 'all' to update all repositories.")]
    public IEnumerable<string> Repositories { get; set; }

    [Option("vmr", Required = false, HelpText = "Path to the VMR; defaults to git root above cwd.")]
    public string VmrPath { get; set; } = Environment.CurrentDirectory;

    [Option("tmp", Required = false, HelpText = "Temporary path where intermediate files are stored (e.g. cloned repos, patch files); defaults to usual TEMP.")]
    public string TmpPath { get; set; }

    [Option('r', "recursive", Required = false, HelpText = $"Process also dependencies (from {VersionFiles.VersionDetailsXml}) recursively.")]
    public bool Recursive { get; set; } = false;
}
