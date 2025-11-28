// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using CommandLine;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

#nullable enable
namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

internal abstract class VmrCommandLineOptionsBase<T> : CommandLineOptions<T> where T : Operation
{
    [Option("vmr", HelpText = "Path to the VMR; defaults to nearest git root above the current working directory.")]
    public string VmrPath { get; set; } = Environment.CurrentDirectory;

    protected void RegisterVmrServices(IServiceCollection services, string? tmpPath)
    {
        if (tmpPath == null)
        {
            tmpPath = Environment.GetEnvironmentVariable("DARC_TMP_DIR");
            if (tmpPath == null)
            {
                tmpPath = new NativePath(Path.GetTempPath()) / Constants.DefaultDarcClonesDirectoryName;
                Directory.CreateDirectory(tmpPath);
            }
        }
        else
        {
            tmpPath = Path.GetFullPath(tmpPath);
        }

        services.AddCodeflow(tmpPath, VmrPath, GitLocation);
        services.TryAddTransient<IVmrScanner, VmrCloakedFileScanner>();
    }
}
