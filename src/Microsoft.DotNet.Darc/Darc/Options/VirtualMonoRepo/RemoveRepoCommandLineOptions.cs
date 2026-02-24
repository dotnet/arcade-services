// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using CommandLine;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

[Verb("remove-repo", HelpText = "Removes repo(s) from the VMR.")]
internal class RemoveRepoCommandLineOptions : VmrCommandLineOptions<RemoveRepoOperation>, IBaseVmrCommandLineOptions
{
    [Value(0, MetaName = "Repository names", Required = true, HelpText = "Repository names to remove from the VMR.")]
    public IEnumerable<string> Repositories { get; set; }

    // Required by IBaseVmrCommandLineOptions but not used for this command
    public IEnumerable<string> AdditionalRemotes { get; set; } = [];

    protected override LogLevel DefaultLogVerbosity => LogLevel.Information;
}
