// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;
using CommandLine;
using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

internal abstract class VmrCommandLineOptions : VmrCommandLineOptionsBase
{
    [Option("tmp", Required = false, HelpText = "Temporary path where intermediate files are stored (e.g. cloned repos, patch files); defaults to usual TEMP.")]
    public string TmpPath { get; set; }

    public override IServiceCollection RegisterServices(IServiceCollection services)
    {
        string tmpPath = Path.GetFullPath(TmpPath ?? Path.GetTempPath());
        LocalSettings localDarcSettings = null;

        var gitHubToken = GitHubPat;
        var azureDevOpsToken = AzureDevOpsPat;

        // Read tokens from local settings if not provided
        // We silence errors because the VMR synchronization often works with public repositories where tokens are not required
        if (gitHubToken == null || azureDevOpsToken == null)
        {
            try
            {
                localDarcSettings = LocalSettings.GetSettings(this, NullLogger.Instance);
            }
            catch (DarcException)
            {
                // The VMR synchronization often works with public repositories where tokens are not required
            }

            gitHubToken ??= localDarcSettings?.GitHubToken;
            azureDevOpsToken ??= localDarcSettings?.AzureDevOpsToken;
        }

        services.AddVmrManagers(GitLocation, VmrPath, tmpPath, gitHubToken, azureDevOpsToken);
        services.TryAddTransient<IVmrScanner, VmrCloakedFileScanner>();
        return base.RegisterServices(services);
    }
}
