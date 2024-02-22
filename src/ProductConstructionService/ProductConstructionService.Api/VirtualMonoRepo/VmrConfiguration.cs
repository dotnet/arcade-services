// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.DataProviders;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.Kusto;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ProductConstructionService.Api.VirtualMonoRepo;

public static class VmrConfiguration
{
    public const string VmrPathKey = "VmrPath";
    public const string TmpPathKey = "TmpPath";
    public const string VmrUriKey = "VmrUri";

    public const string VmrReadyHealthCheckName = "VmrReady";
    public const string VmrReadyHealthCheckTag = "vmrReady";

    public const string KustoDatabaseKey = "KustoDatabase";

    public static void AddVmrRegistrations(this WebApplicationBuilder builder, string vmrPath, string tmpPath, string? vmrUri)
    {
        builder.Services.AddSingleton(new KustoClientProviderOptions
        {
            Database = builder.Configuration[KustoDatabaseKey] ?? throw new ArgumentException($"{KustoDatabaseKey} missing from the configuration"),
            QueryConnectionString = builder.Configuration["nethelix-engsrv-kusto-connection-string-query"]
        });
        builder.Services.AddSingleton<IKustoClientProvider, KustoClientProvider>();
        builder.Services.AddTransient<IBasicBarClient, SqlBarClient>();
        builder.Services.AddVmrManagers(
            "git",
            vmrPath,
            tmpPath,
            builder.Configuration["BotAccount-dotnet-bot-repo-PAT"],
            builder.Configuration["dn-bot-all-orgs-code-r"]);

        if (!builder.Environment.IsDevelopment())
        {
            if (vmrUri == null)
            {
                throw new ArgumentException($"{VmrUriKey} environmental variable must be set");
            }

            builder.Services.AddSingleton(new InitializationBackgroundServiceOptions(vmrUri));
            builder.Services.AddHostedService<InitializationBackgroundService>();
            builder.Services.AddHealthChecks().AddCheck<InitializationHealthCheck>(VmrReadyHealthCheckName, tags: [VmrReadyHealthCheckTag]);
        }
    }
}
