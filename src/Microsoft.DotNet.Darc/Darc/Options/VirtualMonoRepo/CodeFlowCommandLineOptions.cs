// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
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

internal abstract class CodeFlowCommandLineOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>
    : VmrCommandLineOptions<T>, ICodeFlowCommandLineOptions where T : Operation
{
    [RedactFromLogging]
    public IEnumerable<string> AdditionalRemotes { get; set; }

    public string Ref { get; set; }

    public int Build { get; set; }

    public string SubscriptionId { get; set; }

    public string ExcludedAssets { get; set; }

    public bool UnsafeFlow { get; set; }

    public abstract IEnumerable<string> Repositories { get; }

    protected override void Configure()
    {
        AddList(
            "--additional-remote",
            "List of additional remote URIs to add to mappings in the format [mapping name]:[remote URI]. " +
            "Can be used multiple times. " +
            "Example: --additional-remote runtime:https://github.com/myfork/runtime",
            value => AdditionalRemotes = value);
        Add<string>(
            "--ref",
            "Git reference (commit, branch, tag) to flow. " +
                "Defaults to HEAD of the repository in the current working directory. " +
                "Cannot be used together with --build.",
            value => Ref = value);
        Add<int>(
            "--build",
            "ID of the build to flow. " +
                "Cannot be used together with --ref.",
            value => Build = value);
        Add<string>("--subscription", "Subscription ID to simulate. When provided, flows code as the specified subscription would.", value => SubscriptionId = value);
        Add<string>("--excluded-assets", "Semicolon-delineated list of asset filters (package name with asterisks allowed) to be excluded during the flow.", value => ExcludedAssets = value);
        Add<bool>("--unsafe", "Bypasses some codeflow validations. Only use this when recovering from branch flow inconsistencies. This can lead to unexpected or lost changes. Use at your own risk!", value => UnsafeFlow = value);
        base.Configure();
    }

    protected override LogLevel DefaultLogVerbosity => LogLevel.Information;
}
