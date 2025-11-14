// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.DotNet.ProductConstructionService.Client;

namespace ProductConstructionService.Cli.Options;

internal abstract class PcsStatusOptions : Options
{
    [Option("isCi", Required = false, HelpText = "Is running in CI, defaults to false")]
    public required bool IsCi { get; init; } = false;
    [Option("pcsUri", Required = false, HelpText = "PCS base URI, defaults to the Prod PCS")]
    public required string? pcsUri { get; init; }

    public override Task<IServiceCollection> RegisterServices(IServiceCollection services)
    {
        services.AddSingleton(
            string.IsNullOrEmpty(pcsUri) ?
                PcsApiFactory.GetAuthenticated(
                    accessToken: null,
                    managedIdentityId: null,
                    disableInteractiveAuth: IsCi) :
                PcsApiFactory.GetAuthenticated(
                    pcsUri,
                    accessToken: null,
                    managedIdentityId: null,
                    disableInteractiveAuth: IsCi));
        return base.RegisterServices(services);
    }
}
