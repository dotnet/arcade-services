// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using CommandLine;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

internal interface ICodeFlowCommandLineOptions : IBaseVmrCommandLineOptions
{
    string Ref { get; set; }

    public int Build { get; set; }

    public string SubscriptionId { get; set; }

    public string ExcludedAssets { get; set; }

    bool UnsafeFlow { get; set; }
}

internal abstract class CodeFlowCommandLineOptions<T>
    : VmrCommandLineOptions<T>, ICodeFlowCommandLineOptions where T : Operation
{
    [Option("additional-remote", Required = false, HelpText =
        "List of additional remote URIs to add to mappings in the format [mapping name]:[remote URI]. " +
        "Can be used multiple times. " +
        "Example: --additional-remote runtime:https://github.com/myfork/runtime")]
    [RedactFromLogging]
    public IEnumerable<string> AdditionalRemotes { get; set; }

    [Option("ref", Required = false, HelpText = "Git reference (commit, branch, tag) to flow. " +
        "Defaults to HEAD of the repository in the current working directory. " +
        "Cannot be used together with --build.")]
    public string Ref { get; set; }

    [Option("build", Required = false, HelpText = "ID of the build to flow. " +
        "Cannot be used together with --ref.")]
    public int Build { get; set; }

    [Option("subscription", HelpText = "Subscription ID to simulate. When provided, flows code as the specified subscription would.")]
    public string SubscriptionId { get; set; }

    [Option("excluded-assets", HelpText = "Semicolon-delineated list of asset filters (package name with asterisks allowed) to be excluded during the flow.")]
    public string ExcludedAssets { get; set; }

    [Option("unsafe-flow", HelpText = "Ignores problems with codeflow linearity like flowing from a different branch than last time etc. Use at your own risk.")]
    public bool UnsafeFlow { get; set; }

    public abstract IEnumerable<string> Repositories { get; }

    protected override LogLevel DefaultLogVerbosity => LogLevel.Information;
}
