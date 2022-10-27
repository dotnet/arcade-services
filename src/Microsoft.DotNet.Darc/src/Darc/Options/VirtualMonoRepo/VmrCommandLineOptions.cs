// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using CommandLine;
using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

internal abstract class VmrCommandLineOptions : CommandLineOptions
{
    [Option("vmr", Required = true, HelpText = "Path to the VMR; defaults to git root above cwd.")]
    public string VmrPath { get; set; } = Environment.CurrentDirectory;

    [Option("tmp", Required = false, HelpText = "Temporary path where intermediate files are stored (e.g. cloned repos, patch files); defaults to usual TEMP.")]
    public string TmpPath { get; set; }

    public IServiceCollection RegisterServices()
    {
        var services = new ServiceCollection();

        services.TryAddSingleton<IRemoteFactory>(_ => new RemoteFactory(this));
        services.AddVmrManagers(GitLocation, configure: sp =>
        {
            var processManager = sp.GetRequiredService<IProcessManager>();
            var logger = sp.GetRequiredService<ILogger<DarcSettings>>();

            var vmrPath = VmrPath ?? processManager.FindGitRoot(Directory.GetCurrentDirectory());
            var tmpPath = TmpPath ?? LocalSettings.GetDarcSettings(this, logger).TemporaryRepositoryRoot;

            return new VmrInfo(Path.GetFullPath(vmrPath), Path.GetFullPath(tmpPath));
        });

        return services;
    }
}
