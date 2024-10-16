// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using ProductConstructionService.Client;
using ProductConstructionService.Deployment.Operations;

namespace ProductConstructionService.Deployment.Options;

[Verb("get-status", HelpText = "Get PCS status")]
internal class GetStatusOptions : Options
{
    [Option("isCi", Required = false, HelpText = "Is running in CI")]
    public required bool IsCi { get; init; } = false;
    [Option("pcsUri", Required = false, HelpText = "Uri to PCS")]
    public required string? pcsUri { get; init; }

    public override IOperation GetOperation(IServiceProvider sp) => ActivatorUtilities.CreateInstance<GetStatusOperation>(sp);

    public override Task<IServiceCollection> RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<IProductConstructionServiceApi>(
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
