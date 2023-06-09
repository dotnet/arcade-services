// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.Tasks;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace Maestro.Data;

public class BuildAssetRegistryInstallationLookup : IInstallationLookup
{
    private readonly IServiceScopeFactory _scopeFactory;

    public BuildAssetRegistryInstallationLookup(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<long> GetInstallationId(string repositoryUrl)
    {
        using (var scope = _scopeFactory.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<BuildAssetRegistryContext>();
            return await ctx.GetInstallationId(repositoryUrl);
        }
    }

    public Task<bool> IsOrganizationSupported(string org)
    {
        return Task.FromResult(true);
    }
}
