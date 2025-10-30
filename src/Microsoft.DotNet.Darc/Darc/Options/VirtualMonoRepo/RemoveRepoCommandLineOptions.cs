// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using CommandLine;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

[Verb("remove-repo", HelpText = "Removes repo(s) from the VMR.")]
internal class RemoveRepoCommandLineOptions : VmrCommandLineOptions<RemoveRepoOperation>, IBaseVmrCommandLineOptions
{
    [Value(0, Required = true, HelpText = "Repository names to remove from the VMR.")]
    public IEnumerable<string> Repositories { get; set; }

    [Option("additional-remotes", Required = false, HelpText =
        "List of additional remote URIs (not used for removal but required by interface).")]
    [RedactFromLogging]
    public IEnumerable<string> AdditionalRemotes { get; set; }

    [Option("tpn-template", Required = false, HelpText = "Path to a template for regenerating VMRs THIRD-PARTY-NOTICES file. Leave empty to skip generation.")]
    public string TpnTemplate { get; set; }

    [Option("generate-codeowners", Required = false, HelpText = "Regenerate the common CODEOWNERS file for all repositories.")]
    public bool GenerateCodeowners { get; set; } = false;

    [Option("generate-credscansuppressions", Required = false, HelpText = "Regenerate the common .config/CredScanSuppressions.json file for all repositories.")]
    public bool GenerateCredScanSuppressions { get; set; } = false;
}
