// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using CommandLine;
using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

internal abstract class VmrCommandLineOptions : CommandLineOptions
{
    [Option("vmr", Required = true, HelpText = "Path to the VMR; defaults to nearest git root above the current working directory.")]
    public string VmrPath { get; set; } = Environment.CurrentDirectory;

    [Option("tmp", Required = false, HelpText = "Temporary path where intermediate files are stored (e.g. cloned repos, patch files); defaults to usual TEMP.")]
    public string TmpPath { get; set; }

    public IServiceCollection RegisterServices()
    {
        var tmpPath = Path.GetFullPath(TmpPath ?? Path.GetTempPath());
        LocalSettings localDarcSettings = null;

        var gitHubToken = GitHubPat;
        var azureDevOpsToken = AzureDevOpsPat;

        if (gitHubToken == null || azureDevOpsToken == null)
        {
            try
            {
                localDarcSettings = LocalSettings.LoadSettingsFile(this);
            }
            catch (DarcException)
            {
                // The VMR synchronization often works with public repositories where tokens are not required
            }

            gitHubToken = localDarcSettings?.GitHubToken;
            azureDevOpsToken = localDarcSettings?.AzureDevOpsToken;
        }

        var services = new ServiceCollection();
        services
            .AddTransient<GitFileManagerFactory>()
            .AddVmrManagers(
                sp => sp.GetRequiredService<GitFileManagerFactory>(),
                GitLocation,
                VmrPath,
                tmpPath,
                gitHubToken,
                azureDevOpsToken);

        return services;
    }
}
