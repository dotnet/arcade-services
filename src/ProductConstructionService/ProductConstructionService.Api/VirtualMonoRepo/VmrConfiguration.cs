// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib.VirtualMonoRepo;

namespace ProductConstructionService.Api.VirtualMonoRepo;

internal static class VmrConfiguration
{
    private const string VmrPathKey = "VmrPath";
    private const string TmpPathKey = "TmpPath";
    private const string VmrUriKey = "VmrUri";
    private const string VmrReadyHealthCheckName = "VmrReady";
    private const string VmrReadyHealthCheckTag = "vmrReady";

    public static void AddVmrRegistrations(this WebApplicationBuilder builder, string? gitHubToken)
    {
        string vmrPath = builder.Configuration.GetRequiredValue(VmrPathKey);
        string tmpPath = builder.Configuration.GetRequiredValue(TmpPathKey);

        builder.Services.AddVmrManagers("git", vmrPath, tmpPath, gitHubToken, azureDevOpsToken: null);
    }

    public static void AddVmrInitialization(this WebApplicationBuilder builder)
    {
        string vmrUri = builder.Configuration.GetRequiredValue(VmrUriKey);
        builder.Services.AddSingleton(new InitializationBackgroundServiceOptions(vmrUri));
        builder.Services.AddHostedService<InitializationBackgroundService>();
        builder.Services.AddHealthChecks().AddCheck<InitializationHealthCheck>(VmrReadyHealthCheckName, tags: [VmrReadyHealthCheckTag]);
    }

    public static string GetVmrPath(this IConfiguration configuration)
    {
        return configuration.GetRequiredValue(VmrPathKey);
    }
}
