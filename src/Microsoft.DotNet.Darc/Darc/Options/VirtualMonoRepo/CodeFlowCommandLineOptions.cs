// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using CommandLine;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

internal interface ICodeFlowCommandLineOptions : IBaseVmrCommandLineOptions
{
    string Ref { get; set; }

    public int Build { get; }
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

    public abstract IEnumerable<string> Repositories { get; }

    public override IServiceCollection RegisterServices(IServiceCollection services)
    {
        if (!Verbose && !Debug)
        {
            // Force verbose output for these commands
            Verbose = true;
        }

        // Validate that --build and --ref are not used together
        if (Build != 0 && !string.IsNullOrEmpty(Ref))
        {
            throw new System.ArgumentException("The --build and --ref options cannot be used together. Please specify only one.");
        }

        return base.RegisterServices(services);
    }
}
