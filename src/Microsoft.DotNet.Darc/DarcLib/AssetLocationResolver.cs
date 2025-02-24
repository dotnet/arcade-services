// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.ProductConstructionService.Client.Models;

namespace Microsoft.DotNet.DarcLib;

public interface IAssetLocationResolver
{
    /// <summary>
    ///     Update a list of dependencies with asset locations.
    /// </summary>
    /// <param name="dependencies">Dependencies to load locations for</param>
    /// <returns>Async task</returns>
    Task AddAssetLocationToDependenciesAsync(IEnumerable<DependencyDetail> dependencies);
}

public class AssetLocationResolver : IAssetLocationResolver
{
    private readonly IBasicBarClient _barClient;

    public AssetLocationResolver(IBasicBarClient barClient)
    {
        _barClient = barClient;
    }

    public async Task AddAssetLocationToDependenciesAsync(IEnumerable<DependencyDetail> dependencies)
    {
        var buildCache = new Dictionary<int, Build>();

        foreach (var dependency in dependencies)
        {
            IEnumerable<Asset> matchingAssets = await _barClient.GetAssetsAsync(dependency.Name, dependency.Version);
            List<Asset> matchingAssetsFromSameSha = [];

            foreach (var asset in matchingAssets)
            {
                if (!buildCache.TryGetValue(asset.BuildId, out Build producingBuild))
                {
                    producingBuild = await _barClient.GetBuildAsync(asset.BuildId);
                    buildCache.Add(asset.BuildId, producingBuild);
                }

                if (producingBuild.Commit == dependency.Commit)
                {
                    matchingAssetsFromSameSha.Add(asset);
                }
            }

            // Always look at the 'latest' asset to get the right asset even in stable build scenarios
            var latestAsset = matchingAssetsFromSameSha.OrderByDescending(a => a.BuildId).FirstOrDefault();
            if (latestAsset != null)
            {
                IEnumerable<string> currentAssetLocations = latestAsset.Locations?
                    .Where(l => l.Type == LocationType.NugetFeed)
                    .Select(l => l.Location);

                if (currentAssetLocations == null)
                {
                    continue;
                }

                dependency.Locations = currentAssetLocations;
            }
        }
    }
}
