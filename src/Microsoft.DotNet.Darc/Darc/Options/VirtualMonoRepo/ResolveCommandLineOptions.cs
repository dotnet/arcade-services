// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.IO;
using CommandLine;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

[Verb("resolve", HelpText = "Allows the user to resolve codeflow conflicts encountered by Maestro.")]
internal class ResolveCommandLineOptions : CodeFlowCommandLineOptions<ResolveOperation>
{
    [Value(0, Required = true, HelpText = "Path to a local repository to flow the current VMR commit to")]
    public string Repository { get; set; }

    [Value(1, Required = true, HelpText = "Local path to the cloned git repository that is the source of the codeflow")]
    public string SourceRepo { get; set; }

    [Value(2, Required = true, HelpText = "Subscription id")]
    public string SubscriptionId { get; set; }

    public override IEnumerable<string> Repositories => [Path.GetFileName(Repository) + ":" + Repository];
}
