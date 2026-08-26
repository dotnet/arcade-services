// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Common;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ProductConstructionService.ReproTool.Options;
using GitHubClient = Octokit.GitHubClient;

namespace ProductConstructionService.ReproTool.Operations;

internal class CopyBuildOperation(
        CopyBuildOptions options,
        IBarApiClient prodBarClient,
        [FromKeyedServices("local")] IProductConstructionServiceApi localPcsApi,
        GitHubClient ghClient,
        ILogger<CopyBuildOperation> logger)
    : Operation(logger, ghClient, localPcsApi)
{
    internal override async Task RunAsync()
    {
        logger.LogInformation("Fetching production BAR build {buildId}", options.BuildId);

        Build productionBuild = await prodBarClient.GetBuildAsync(
            options.BuildId,
            includeAssetLocation: true);

        string productionRepository = productionBuild.GetRepository();
        (string repositoryName, _) = GitRepoUrlUtils.GetRepoNameAndOwner(productionRepository);
        string localRepository = $"https://github.com/{MaestroAuthTestOrgName}/{repositoryName}";

        List<AssetData> assets = productionBuild.Assets
            .Select(asset => new AssetData(asset.NonShipping)
            {
                Name = asset.Name,
                Version = asset.Version,
                Locations = asset.Locations?
                    .Select(location => new AssetLocationData(location.Type)
                    {
                        Location = location.Location
                    })
                    .ToList()
            })
            .ToList();

        Build localBuild = await CreateBuildAsync(
            localRepository,
            productionBuild.GetBranch(),
            productionBuild.Commit,
            assets);

        logger.LogInformation(
            "Created local BAR build {localBuildId} from production build {productionBuildId} with {assetCount} assets",
            localBuild.Id,
            productionBuild.Id,
            assets.Count);
    }
}
