// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.DotNet.ProductConstructionService.Client;
using ProductConstructionService.Cli.Operations;

namespace ProductConstructionService.Cli.Options;

/// <summary>
/// Base options class for operations that need to communicate with the Product Construction Service API.
/// </summary>
internal abstract class PcsApiOptions : Options
{
    [Option("isCi", Required = false, HelpText = "Is running in CI, defaults to false")]
    public required bool IsCi { get; init; } = false;

    [Option("pcsUri", Required = false, HelpText = "PCS base URI, defaults to the Prod PCS")]
    public required string? PcsUri { get; init; }

    public override Task<IServiceCollection> RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<IProductConstructionServiceApi>(
            string.IsNullOrEmpty(PcsUri) ?
                PcsApiFactory.GetAuthenticated(
                    accessToken: null,
                    managedIdentityId: null,
                    disableInteractiveAuth: IsCi) :
                PcsApiFactory.GetAuthenticated(
                    PcsUri,
                    accessToken: null,
                    managedIdentityId: null,
                    disableInteractiveAuth: IsCi));
        services.AddSingleton<ISubscriptionDescriptionHelper, SubscriptionDescriptionHelper>();
        return base.RegisterServices(services);
    }
}
