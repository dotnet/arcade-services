// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;

namespace FlatFlowMigrationCli.Options;

internal abstract class FlatFlowMigrationCliOptions : Options
{
    [Option("pcsUri", Required = false, Default = "https://maestro.dot.net/", HelpText = "PCS base URI, defaults to the Prod PCS")]
    public required string PcsUri { get; init; }

    [Option("vmr", Required = false, Default = "https://github.com/dotnet/dotnet", HelpText = "URI or path to the VMR. Defaults to https://github.com/dotnet/dotnet")]
    public required string VmrUri { get; init; }

    [Option("tmp", Required = false, HelpText = "Temporary path where intermediate files are stored (e.g. cloned repos, patch files); defaults to usual TEMP.")]
    public string? TmpPath { get; set; }

    public override Task<IServiceCollection> RegisterServices(IServiceCollection services)
    {
        services.AddSingleton(PcsApiFactory.GetAuthenticated(
            PcsUri,
            accessToken: null,
            managedIdentityId: null,
            disableInteractiveAuth: false));

        TmpPath ??= Path.GetTempPath();

        services.AddMultiVmrSupport(TmpPath);

        return base.RegisterServices(services);
    }
}
