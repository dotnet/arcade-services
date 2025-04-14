// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using FlatFlowMigrationCli.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace FlatFlowMigrationCli.Options;

/// <summary>
/// Options for the rollback operation.
/// </summary>
[Verb("rollback", HelpText = "Reverts changes made by a previous migration operation.")]
internal class RollbackOptions : Options
{
    /// <summary>
    /// Path to the log file generated during the migration operation.
    /// </summary>
    [Option('l', "log-file", Required = true, HelpText = "Path to the migration log file.")]
    public string LogFilePath { get; set; } = string.Empty;

    public override Task<IServiceCollection> RegisterServices(IServiceCollection services)
    {
        services.AddTransient<ISubscriptionMigrator, SubscriptionMigrator>();
        return base.RegisterServices(services);
    }

    public override IOperation GetOperation(IServiceProvider sp) => ActivatorUtilities.CreateInstance<RollbackOperation>(sp, this);
}
