// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

[Verb("reset", HelpText = "Resets the contents of a VMR mapping to match a specific commit SHA from the source repository.")]
internal class ResetCommandLineOptions : VmrCommandLineOptions<ResetOperation>
{
    [Value(0, Required = true, HelpText = 
        "Repository mapping and target SHA in the format [mapping]:[sha]. " +
        "Example: runtime:abc123def456 will reset the runtime mapping to commit abc123def456.")]
    public string MappingAndSha { get; set; }
}