// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using CommandLine;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

internal abstract class VmrSyncCommandLineOptions : VmrCommandLineOptions
{
    [Option("additional-remotes", Required = false, HelpText =
        "Comma separated list of additional remote URIs to add to mappings in the format [mapping name]:[remote URI]. " +
        "Example: installer:https://github.com/myfork/installer,sdk:/local/path/to/sdk")]
    public string AdditionalRemotes { get; set; }
    
    [Value(0, Required = true, HelpText =
        "Repository names in the form of NAME or NAME:REVISION where REVISION is a commit SHA or other git reference (branch, tag). " +
        "Omitting REVISION will synchronize the repo to current HEAD.")]
    public IEnumerable<string> Repositories { get; set; }

    [Option("readme-template", Required = false, HelpText = "Path to a template for generating VMRs README file. Leave empty to skip generation.")]
    public string ReadMeTemplate { get; set; }

    [Option("tpn-template", Required = false, HelpText = "Path to a template for generating VMRs THIRD-PARTY-NOTICES file. Leave empty to skip generation.")]
    public string TpnTemplate { get; set; }
}
