// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

internal class RemoveRepoCommandLineOptions : VmrCommandLineOptions<RemoveRepoOperation>, IBaseVmrCommandLineOptions
{
    public override string Verb => "remove-repo";

    public override string VerbDescription => "Removes repo(s) from the VMR.";

    public IEnumerable<string> Repositories { get; set; }

    // Required by IBaseVmrCommandLineOptions but not used for this command
    public IEnumerable<string> AdditionalRemotes { get; set; } = [];

    protected override LogLevel DefaultLogVerbosity => LogLevel.Information;

    protected override void Configure()
    {
        AddValueList(
            "Repository names",
            "Repository names to remove from the VMR.",
            true,
            value => Repositories = value);
        base.Configure();
    }
}
