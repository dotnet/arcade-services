// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.DataProviders;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ProductConstructionService.Api;

public static class VmrConfiguration
{
    public const string VmrPathKey = "VmrPath";
    public const string TmpPathKey = "TmpPath";
    public const string VmrUriKey = "VmrUri";

    public static void AddVmrRegistrations(this WebApplicationBuilder builder)
    {
        builder.Services.TryAddSingleton<IBasicBarClient, SqlBarClient>();
        builder.Services.AddVmrManagers(
            "git",
            Environment.GetEnvironmentVariable(VmrPathKey) ?? throw new ArgumentException($"{VmrPathKey} environmental variable must be set"),
            Environment.GetEnvironmentVariable(TmpPathKey) ?? throw new ArgumentException($"{TmpPathKey} environmental variable must be set"),
            builder.Configuration["BotAccount-dotnet-bot-repo-PAT"],
            builder.Configuration["dn-bot-all-orgs-code-r"]);

        if (!builder.Environment.IsDevelopment())
        {
            builder.Services.AddTransient<IStartupFilter>(sp =>
                ActivatorUtilities.CreateInstance<VmrCloneStartupFilter>(
                    sp,
                    Environment.GetEnvironmentVariable(VmrUriKey) ?? throw new ArgumentException($"{VmrUriKey} environmental variable must be set")));
        }
    }
}
