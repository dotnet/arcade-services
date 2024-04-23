// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using ProductConstructionService.Api.Configuration;

namespace ProductConstructionService.Api.VirtualMonoRepo;

public static class VmrConfiguration
{
    public const string VmrPathKey = "VmrPath";
    public const string TmpPathKey = "TmpPath";
    public const string VmrUriKey = "VmrUri";

    public const string VmrReadyHealthCheckName = "VmrReady";
    public const string VmrReadyHealthCheckTag = "vmrReady";

    public static void AddVmrRegistrations(this WebApplicationBuilder builder, string vmrPath, string tmpPath)
    {
        builder.Services.AddVmrManagers(
            "git",
            vmrPath,
            tmpPath,
            builder.Configuration[PcsConfiguration.GitHubToken],
            builder.Configuration[PcsConfiguration.AzDOToken]);
    }

    public static void AddVmrInitialization(this WebApplicationBuilder builder, string vmrUri)
    {
        builder.Services.AddSingleton(new InitializationBackgroundServiceOptions(vmrUri));
        builder.Services.AddHostedService<InitializationBackgroundService>();
        builder.Services.AddHealthChecks().AddCheck<InitializationHealthCheck>(VmrReadyHealthCheckName, tags: [VmrReadyHealthCheckTag]);
    }
}
