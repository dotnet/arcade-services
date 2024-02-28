﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.DataProviders;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ProductConstructionService.Api.VirtualMonoRepo;

public static class VmrConfiguration
{
    public const string VmrPathKey = "VmrPath";
    public const string TmpPathKey = "TmpPath";
    public const string VmrUriKey = "VmrUri";

    public const string VmrReadyHealthCheckName = "VmrReady";
    public const string VmrReadyHealthCheckTag = "vmrReady";

    public static void AddVmrRegistrations(this WebApplicationBuilder builder, string vmrPath, string tmpPath, string vmrUri)
    {
        builder.Services.TryAddTransient<IBasicBarClient, SqlBarClient>();
        builder.Services.AddVmrManagers(
            "git",
            vmrPath,
            tmpPath,
            builder.Configuration[PcsConfiguration.GitHubToken],
            builder.Configuration[PcsConfiguration.AzDOToken]);

        if (!builder.Environment.IsDevelopment())
        {
            builder.Services.AddSingleton(new InitializationBackgroundServiceOptions(vmrUri));
            builder.Services.AddHostedService<InitializationBackgroundService>();
            builder.Services.AddHealthChecks().AddCheck<InitializationHealthCheck>(VmrReadyHealthCheckName, tags: [VmrReadyHealthCheckTag]);
        }
    }
}
