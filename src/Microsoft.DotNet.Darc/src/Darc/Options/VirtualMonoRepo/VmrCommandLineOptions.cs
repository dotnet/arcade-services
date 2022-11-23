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
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        string gitHubToken = null;
        string azureDevOpsToken = null;

        try
        {
            var localDarcSettings = LocalSettings.LoadSettingsFile(this);
            gitHubToken = GitHubPat ?? (localDarcSettings?.GitHubToken);
            azureDevOpsToken = AzureDevOpsPat ?? (localDarcSettings?.AzureDevOpsToken);
        }
        catch (DarcException)
        {

        }

        var services = new ServiceCollection();

        services.TryAddSingleton<IRemoteFactory>(_ => new RemoteFactory(this));

        services.AddVmrManagers(GitLocation, VmrPath, tmpPath, configure: sp =>
        {
            return new VmrRemoteConfiguration(gitHubToken, azureDevOpsToken);
        });

        return services;
    }
}
