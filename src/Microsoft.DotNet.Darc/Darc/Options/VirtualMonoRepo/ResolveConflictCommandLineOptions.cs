// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using CommandLine;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

[Verb("resolve-conflict", HelpText = "Allows the user to resolve codeflow conflicts encountered by Maestro.")]
internal class ResolveConflictCommandLineOptions : CodeFlowCommandLineOptions<ResolveConflictOperation>
{
    [Value(0, Required = true, HelpText = "Subscription id")]
    public string SubscriptionId { get; set; }

    public override IEnumerable<string> Repositories => []; // we only want to synchronize current HEAD
}
