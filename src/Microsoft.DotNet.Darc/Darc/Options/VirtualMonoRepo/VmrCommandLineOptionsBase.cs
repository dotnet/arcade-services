// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using CommandLine;
using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

#nullable enable
namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

internal abstract class VmrCommandLineOptionsBase<T> : CommandLineOptions<T> where T : Operation
{
    [Option("vmr", HelpText = "Path to the VMR; defaults to nearest git root above the current working directory.")]
    public string VmrPath { get; set; } = Environment.CurrentDirectory;

    protected void RegisterVmrServices(IServiceCollection services, string? tmpPath)
    {
        LocalSettings? localDarcSettings = null;

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

        tmpPath = Path.GetFullPath(tmpPath ?? Path.GetTempPath());

        services.AddSingleVmrSupport(GitLocation, VmrPath, tmpPath, gitHubToken, azureDevOpsToken);
        services.TryAddTransient<IVmrScanner, VmrCloakedFileScanner>();
    }
}
