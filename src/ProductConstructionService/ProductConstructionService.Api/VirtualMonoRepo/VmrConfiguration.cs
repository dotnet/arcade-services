// Licensed to the .NET Foundation under one or more agreements.
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

    public const string VmrClonedHealthCheckTag = "vmrCloned";

    public static void AddVmrRegistrations(this WebApplicationBuilder builder, string vmrPath, string tmpPath, string vmrUri)
    {
        builder.Services.TryAddSingleton<IBasicBarClient, SqlBarClient>();
        builder.Services.AddVmrManagers(
            "git",
            vmrPath,
            tmpPath,
            builder.Configuration["BotAccount-dotnet-bot-repo-PAT"],
            builder.Configuration["dn-bot-all-orgs-code-r"]);

        if (!builder.Environment.IsDevelopment())
        {
            builder.Services.AddSingleton(new VmrClonerBackgroundServiceOptions(vmrUri));
            builder.Services.AddHostedService<VmrClonerBackgroundService>();
            builder.Services.AddHealthChecks().AddCheck<VmrClonedHealthCheck>("VmrCloned", tags: [VmrClonedHealthCheckTag]);
        }
    }
}
