// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

internal class AddRepoCommandLineOptions : VmrCommandLineOptions<AddRepoOperation>, IBaseVmrCommandLineOptions
{
    public override string Verb => "add-repo";

    public override string VerbDescription => "Adds new repo(s) to the VMR that haven't been synchronized yet.";

    public IEnumerable<string> Repositories { get; set; }

    // Required by IBaseVmrCommandLineOptions but not used for this command
    public IEnumerable<string> AdditionalRemotes { get; set; } = [];

    protected override LogLevel DefaultLogVerbosity => LogLevel.Information;

    protected override void Configure()
    {
        AddValueList(
            "Repository URIs",
            "Repository URIs in the form of URI:REVISION where URI is the git repository URL (e.g., https://github.com/dotnet/runtime) and REVISION is a commit SHA or other git reference (branch, tag).",
            true,
            value => Repositories = value);
        base.Configure();
    }
}
