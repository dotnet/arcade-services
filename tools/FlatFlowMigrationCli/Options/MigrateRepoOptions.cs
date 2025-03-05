// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using FlatFlowMigrationCli.Operations;

namespace FlatFlowMigrationCli.Options;

[Verb("migrate-repo", HelpText = "Onboards a repository to a flat dependency flow")]
internal class MigrateRepoOptions : FlatFlowMigrationCliOptions
{
    [Option('r', "repo", Required = true, HelpText = "Name of the repository to migrate (should correspond to a source mapping)")]
    public required string? Mapping { get; init; }

    public override IOperation GetOperation(IServiceProvider sp) => ActivatorUtilities.CreateInstance<MigrateRepoOperation>(sp, this);
}
