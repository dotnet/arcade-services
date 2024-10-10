// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using ProductConstructionService.Common;

namespace ProductConstructionService.Api.VirtualMonoRepo;

internal static class VmrConfiguration
{
    private const string VmrPathKey = "VmrPath";
    private const string TmpPathKey = "TmpPath";
    private const string VmrUriKey = "VmrUri";
    private const string VmrReadyHealthCheckName = "VmrReady";
    private const string VmrReadyHealthCheckTag = "vmrReady";

    public static void AddVmrRegistrations(this WebApplicationBuilder builder)
    {
        string vmrPath = builder.Configuration.GetRequiredValue(VmrPathKey);
        string tmpPath = builder.Configuration.GetRequiredValue(TmpPathKey);

        builder.Services.AddVmrManagers("git", vmrPath, tmpPath, gitHubToken: null, azureDevOpsToken: null);
    }

    public static void InitializeVmrFromRemote(this WebApplicationBuilder builder)
    {
        string vmrUri = builder.Configuration.GetRequiredValue(VmrUriKey);
        builder.Services.AddSingleton(new InitializationBackgroundServiceOptions(vmrUri));
        builder.Services.AddHostedService<InitializationBackgroundService>();
        builder.Services.AddHealthChecks().AddCheck<InitializationHealthCheck>(VmrReadyHealthCheckName, tags: [VmrReadyHealthCheckTag]);
    }

    public static void InitializeVmrFromDisk(this WebApplicationBuilder builder)
    {
        // This is expected in local flows and it's useful to learn about this early
        var vmrPath = builder.Configuration.GetRequiredValue(VmrPathKey);
        if (!Directory.Exists(vmrPath))
        {
            throw new InvalidOperationException($"VMR not found at {vmrPath}. " +
                $"Either run the service in initialization mode or clone a VMR into {vmrPath}.");
        }

        // TODO: Change IVmrInfo to be loaded from configurations and call Configure() here
        var vmrInfo = builder.Services.BuildServiceProvider().GetRequiredService<IVmrInfo>();
        vmrInfo.VmrUri = builder.Configuration.GetRequiredValue(VmrUriKey);
    }
}
