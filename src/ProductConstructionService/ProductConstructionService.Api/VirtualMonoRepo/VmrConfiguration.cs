// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Common;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;

namespace ProductConstructionService.Api.VirtualMonoRepo;

internal static class VmrConfiguration
{
    private const string TmpPathKey = "TmpPath";
    private const string VmrUriKey = "VmrUri";
    private const string VmrReadyHealthCheckName = "VmrReady";
    private const string VmrReadyHealthCheckTag = "vmrReady";

    public static void AddCodeflow(this WebApplicationBuilder builder)
    {
        string tmpPath = builder.Configuration.GetRequiredValue(TmpPathKey);
        builder.Services.AddCodeflow(tmpPath);
    }

    public static void InitializeVmrFromRemote(this WebApplicationBuilder builder)
    {
        string vmrUri = builder.Configuration.GetRequiredValue(VmrUriKey);
        builder.Services.AddSingleton(new InitializationBackgroundServiceOptions(vmrUri));
        builder.Services.AddHostedService<InitializationBackgroundService>();
        builder.Services.AddHealthChecks().AddCheck<InitializationHealthCheck>(VmrReadyHealthCheckName, tags: [VmrReadyHealthCheckTag]);
    }
}
