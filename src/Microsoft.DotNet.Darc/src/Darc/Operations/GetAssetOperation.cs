// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Darc.Operations
{
    /// <summary>
    ///     Retrieve information about a specific asset by name.
    /// </summary>
    internal class GetAssetOperation : Operation
    {
        private GetAssetCommandLineOptions _options;

        public GetAssetOperation(GetAssetCommandLineOptions options)
            : base(options)
        {
            _options = options;
        }

        public override async Task<int> ExecuteAsync()
        {
            IRemote remote = RemoteFactory.GetBarOnlyRemote(_options, Logger);

            try
            {
                Channel targetChannel = null;
                if (!string.IsNullOrEmpty(_options.Channel))
                {
                    targetChannel = await UxHelpers.ResolveSingleChannel(remote, _options.Channel);
                    if (targetChannel == null)
                    {
                        return Constants.ErrorCode;
                    }
                }

                // Starting with the remote, get information on the asset name + version
                List<Asset> matchingAssets =
                    (await remote.GetAssetsAsync(name: _options.Name, version: _options.Version)).ToList();

                string queryDescriptionString =
                        $"name '{_options.Name}'{(!string.IsNullOrEmpty(_options.Version) ? $" and version '{_options.Version}'" : "")}" +
                        $"{(targetChannel != null ? $" on channel '{targetChannel.Name}'" : "")} in the last {_options.MaxAgeInDays} days";

                // Only print the lookup string if the output type is text.
                if (_options.OutputFormat == DarcOutputType.text)
                {
                    Console.WriteLine($"Looking up assets with {queryDescriptionString}");
                }

                // Walk the assets and look up the corresponding builds, potentially filtering based on channel
                // if there is a target channel
                int maxAgeInDays = _options.MaxAgeInDays;
                var now = DateTimeOffset.Now;
                int checkedAssets = 0;

                List<(Asset asset, Build build)> matchingAssetsAfterDate = new List<(Asset, Build)>();

                foreach (Asset asset in matchingAssets)
                {
                    // Get build info for asset
                    Build buildInfo = await remote.GetBuildAsync(asset.BuildId);

                    if (now.Subtract(buildInfo.DateProduced).TotalDays > maxAgeInDays)
                    {
                        break;
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
                    Console.WriteLine($"No assets found with {queryDescriptionString}");
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
                Logger.LogError(e, "Error: Failed to retrieve information about assets.");
                return Constants.ErrorCode;
            }
        }
    }
}
