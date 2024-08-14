// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Maestro.Client;

namespace ProductConstructionService.Api.Configuration;

// TODO (https://github.com/dotnet/arcade-services/issues/3807): Won't be needed but keeping here to make PCS happy for now
internal static class MaestroApiConfiguration
{
    private const string MaestroUri = "Maestro:Uri";
    private const string MaestroNoAuth = "Maestro:NoAuth";

    public static void AddMaestroApiClient(this WebApplicationBuilder builder, string? managedIdentityId)
    {
        builder.Services.AddScoped<IMaestroApi>(s =>
        {
            var uri = builder.Configuration[MaestroUri]
                ?? throw new Exception($"Missing configuration key {MaestroUri}");

            var noAuth = builder.Configuration.GetValue<bool>(MaestroNoAuth);
            if (noAuth)
            {
                return MaestroApiFactory.GetAnonymous(uri);
            }

            return MaestroApiFactory.GetAuthenticated(
                uri,
                accessToken: null,
                managedIdentityId: managedIdentityId,
                disableInteractiveAuth: true);
        });
    }
}
