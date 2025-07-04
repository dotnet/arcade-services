// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using CommandLine;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

[Verb("initialize", HelpText = "Initializes new repo(s) that haven't been synchronized into the VMR yet.")]
internal class InitializeCommandLineOptions : VmrCommandLineOptions<InitializeOperation>, IBaseVmrCommandLineOptions
{
    [Option("source-mappings", Required = true, HelpText = $"A path to the {VmrInfo.SourceMappingsFileName} file to be used for syncing.")]
    public string SourceMappings { get; set; }

    [Option("additional-remotes", Required = false, HelpText =
        "List of additional remote URIs to add to mappings in the format [mapping name]:[remote URI]. " +
        "Example: installer:https://github.com/myfork/installer sdk:/local/path/to/sdk")]
    [RedactFromLogging]
    public IEnumerable<string> AdditionalRemotes { get; set; }

    [Value(0, Required = true, HelpText =
        "Repository names in the form of NAME or NAME:REVISION where REVISION is a commit SHA or other git reference (branch, tag). " +
        "Omitting REVISION will synchronize the repo to current HEAD.")]
    public IEnumerable<string> Repositories { get; set; }

    [Option("tpn-template", Required = false, HelpText = "Path to a template for generating VMRs THIRD-PARTY-NOTICES file. Leave empty to skip generation.")]
    public string TpnTemplate { get; set; }

    [Option("generate-codeowners", Required = false, HelpText = "Generate a common CODEOWNERS file for all repositories.")]
    public bool GenerateCodeowners { get; set; } = false;

    [Option("generate-credscansuppressions", Required = false, HelpText = "Generate a common .config/CredScanSuppressions.json file for all repositories.")]
    public bool GenerateCredScanSuppressions { get; set; } = false;
}
