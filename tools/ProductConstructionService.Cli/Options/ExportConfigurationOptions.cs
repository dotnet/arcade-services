// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.DependencyInjection;
using ProductConstructionService.Cli.Operations;

namespace ProductConstructionService.Cli.Options;

[Verb("export-configuration", HelpText = "Export PCS subscription configuration into yaml")]
internal class ExportConfigurationOptions : PcsStatusOptions
{
    [Option("export-path", Required = false, HelpText = "The output path for the exported configuration files.")]
    public required string ExportPath { get; set; } = Directory.GetCurrentDirectory();

    public override Task<IServiceCollection> RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<IFileSystem, FileSystem>();
        return base.RegisterServices(services);
    }

    public override IOperation GetOperation(IServiceProvider sp) => ActivatorUtilities.CreateInstance<ExportConfigurationOperation>(sp, this);
}
