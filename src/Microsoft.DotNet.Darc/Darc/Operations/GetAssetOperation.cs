// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Darc.Operations;

/// <summary>
///     Retrieve information about a specific asset by name.
/// </summary>
internal class GetAssetOperation : Operation
{
    private readonly GetAssetCommandLineOptions _options;
    private readonly IBarApiClient _barClient;
    private readonly ILogger<GetAssetOperation> _logger;

    public GetAssetOperation(
        GetAssetCommandLineOptions options,
        IBarApiClient barClient,
        ILogger<GetAssetOperation> logger)
    {
        _options = options;
        _barClient = barClient;
        _logger = logger;
    }

    public override async Task<int> ExecuteAsync()
    {
        if (_options.Name == null && _options.Build == null)
        {
            Console.WriteLine("You need to specify either an asset name or a build");
            return Constants.ErrorCode;
        }

        try
        {
            Channel targetChannel = null;
            if (!string.IsNullOrEmpty(_options.Channel))
            {
                targetChannel = await UxHelpers.ResolveSingleChannel(_barClient, _options.Channel);
                if (targetChannel == null)
                {
                    return Constants.ErrorCode;
                }
            }

            // Starting with the remote, get information on the asset name + version
            List<Asset> matchingAssets =
                (await _barClient.GetAssetsAsync(name: _options.Name, version: _options.Version, buildId: _options.Build)).ToList();

            var queryDescription = new StringBuilder();

            if (!string.IsNullOrEmpty(_options.Name))
            {
                queryDescription.Append($" named '{_options.Name}'");
            }

            if (_options.Build.HasValue)
            {
                queryDescription.Append($" from build '{_options.Build}'");
            }

            if (!string.IsNullOrEmpty(_options.Version))
            {
                queryDescription.Append($" with version '{_options.Version}'");
            }

            if (targetChannel != null)
            {
                queryDescription.Append($" on channel '{targetChannel.Name}'");
            }

            if (!_options.Build.HasValue)
            {
                queryDescription.Append($" in the last {_options.MaxAgeInDays} days");
            }

            // Only print the lookup string if the output type is text.
            if (_options.OutputFormat == DarcOutputType.text)
            {
                Console.WriteLine($"Looking up assets{queryDescription}");
            }

            // Walk the assets and look up the corresponding builds, potentially filtering based on channel
            // if there is a target channel
            int maxAgeInDays = _options.MaxAgeInDays;
            var now = DateTimeOffset.Now;
            int checkedAssets = 0;

            List<(Asset asset, Build build)> matchingAssetsAfterDate = [];

            Build buildInfo = null;
            if (_options.Build.HasValue)
            {
                buildInfo = await _barClient.GetBuildAsync(_options.Build.Value);
            }

            foreach (Asset asset in matchingAssets)
            {
                // Get build info for asset
                if (!_options.Build.HasValue)
                {
                    buildInfo = await _barClient.GetBuildAsync(asset.BuildId);
                    if (now.Subtract(buildInfo.DateProduced).TotalDays > maxAgeInDays)
                    {
                        break;
                    }
                }

                checkedAssets++;

                if (targetChannel != null && !buildInfo.Channels.Any(c => c.Id == targetChannel.Id))
                {
                    continue;
                }

                matchingAssetsAfterDate.Add((asset, buildInfo));
            }

            if (!matchingAssetsAfterDate.Any())
            {
                Console.WriteLine($"No assets found with {queryDescription}");
                int remaining = matchingAssets.Count - checkedAssets;
                if (remaining > 0)
                {
                    Console.WriteLine($"Skipping build lookup for {remaining} assets. Consider increasing --max-age to check the rest.");
                }

                return Constants.ErrorCode;
            }

            switch (_options.OutputFormat)
            {
                case DarcOutputType.text:
                    foreach ((Asset asset, Build build) in matchingAssetsAfterDate)
                    {
                        Console.WriteLine($"{asset.Name} @ {asset.Version}");
                        Console.Write(UxHelpers.GetTextBuildDescription(build));
                        Console.WriteLine("Locations:");
                        if (asset.Locations.Any())
                        {
                            foreach (var location in asset.Locations)
                            {
                                if (location.IsValid)
                                {
                                    Console.WriteLine($"- {location.Location} ({location.Type})");
                                }
                            }
                        }
                        else
                        {
                            Console.WriteLine("- None");
                        }
                        Console.WriteLine();
                    }
                    break;
                case DarcOutputType.json:
                    var assets = matchingAssetsAfterDate.Select(assetAndBuild =>
                    {
                        return new
                        {
                            name = assetAndBuild.asset.Name,
                            version = assetAndBuild.asset.Version,
                            build = UxHelpers.GetJsonBuildDescription(assetAndBuild.build),
                            locations = assetAndBuild.asset.Locations.Select(location => location.Location)
                        };
                    });
                    Console.WriteLine(JsonConvert.SerializeObject(assets, Formatting.Indented));
                    break;
            }

            return Constants.SuccessCode;
        }
        catch (AuthenticationException e)
        {
            Console.WriteLine(e.Message);
            return Constants.ErrorCode;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error: Failed to retrieve information about assets.");
            return Constants.ErrorCode;
        }
    }
}
