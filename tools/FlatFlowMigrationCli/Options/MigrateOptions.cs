﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.DependencyInjection;
using FlatFlowMigrationCli.Operations;

namespace FlatFlowMigrationCli.Options;

[Verb("migrate", HelpText = "Onboards VMR repositories onto the flat dependency flow")]
internal class MigrateOptions : Options
{
    [Option("pcsUri", Required = false, Default = "https://maestro.dot.net/", HelpText = "PCS base URI, defaults to the Prod PCS")]
    public required string PcsUri { get; init; }

    [Option("vmr", Required = false, Default = "https://github.com/dotnet/dotnet", HelpText = "URI or path to the VMR. Defaults to https://github.com/dotnet/dotnet")]
    public required string VmrUri { get; init; }

    [Option("perform-updates", Required = false, Default = false, HelpText = "If not supplied, performs a dry run only which logs actions instead of performing them.")]
    public bool PerformUpdates { get; init; }

    [Option("output", Required = false, Default = "migration.log", HelpText = "Path where a migration log will be stored (when dry-running the tool)")]
    public string OutputPath { get; init; } = "migration.log";

    public override Task<IServiceCollection> RegisterServices(IServiceCollection services)
    {
        services.AddSingleton(PcsApiFactory.GetAuthenticated(
            PcsUri,
            accessToken: null,
            managedIdentityId: null,
            disableInteractiveAuth: false));

        if (PerformUpdates)
        {
            services.AddTransient<ISubscriptionMigrator, SubscriptionMigrator>();
        }
        else
        {
            services.AddTransient<ISubscriptionMigrator>(sp => ActivatorUtilities.CreateInstance<MigrationLogger>(sp, OutputPath));
        }

        services.AddMultiVmrSupport(Path.GetTempPath());

        return base.RegisterServices(services);
    }

    public override IOperation GetOperation(IServiceProvider sp) => ActivatorUtilities.CreateInstance<MigrateOperation>(sp, this);
}
