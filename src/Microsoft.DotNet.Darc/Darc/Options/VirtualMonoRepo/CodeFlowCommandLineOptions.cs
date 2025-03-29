﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using CommandLine;
using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

internal interface ICodeFlowCommandLineOptions : IBaseVmrCommandLineOptions
{
    string Ref { get; set; }
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
        "Defaults to HEAD of the repository in the current working directory.")]
    public string Ref { get; set; }

    public abstract IEnumerable<string> Repositories { get; }

    public override IServiceCollection RegisterServices(IServiceCollection services)
    {
        if (!Verbose && !Debug)
        {
            // Force verbose output for these commands
            Verbose = true;
        }

        base.RegisterServices(services);
        services.AddTransient<IDarcVmrForwardFlower, DarcVmrForwardFlower>();
        services.AddTransient<IDarcVmrBackFlower, DarcVmrBackFlower>();
        return services;
    }
}
