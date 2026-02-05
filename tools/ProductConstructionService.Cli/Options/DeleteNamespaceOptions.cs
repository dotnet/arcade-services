// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using ProductConstructionService.Cli.Operations;

namespace ProductConstructionService.Cli.Options;

[Verb("delete-namespace", HelpText = "Delete a namespace from PCS configuration")]
internal class DeleteNamespaceOptions : PcsApiOptions
{
    [Option("namespace", Required = true, HelpText = "The name of the namespace to delete.")]
    public required string NamespaceName { get; init; }

    [Option("save-changes", Required = false, Default = false, HelpText = "Whether to persist the changes. Defaults to false (dry run).")]
    public required bool SaveChanges { get; init; }

    public override IOperation GetOperation(IServiceProvider sp) => ActivatorUtilities.CreateInstance<DeleteNamespaceOperation>(sp, this);
}
