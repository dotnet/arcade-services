// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations;

internal class GetBuildOperation : Operation
{
    private readonly GetBuildCommandLineOptions _options;
    private readonly IBarApiClient _barClient;
    private readonly ILogger<GetBuildOperation> _logger;

    public GetBuildOperation(
        GetBuildCommandLineOptions options,
        IBarApiClient barClient,
        ILogger<GetBuildOperation> logger)
    {
        _options = options;
        _barClient = barClient;
        _logger = logger;
    }

    /// <summary>
    ///     Get a specific build of a repository
    /// </summary>
    /// <returns>Process exit code.</returns>
    public override async Task<int> ExecuteAsync()
    {
        var aBefore = JsonFlattener.FlattenJsonToDictionary(File.ReadAllText("C:\\Users\\dkurepa\\Desktop\\MOT outputs - Copy\\aBefore.json"));
        var aAfter = JsonFlattener.FlattenJsonToDictionary(File.ReadAllText("C:\\Users\\dkurepa\\Desktop\\MOT outputs - Copy\\aAfter.json"));
        var bBefore = JsonFlattener.FlattenJsonToDictionary(File.ReadAllText("C:\\Users\\dkurepa\\Desktop\\MOT outputs - Copy\\bBefore.json"));
        var bAfter = JsonFlattener.FlattenJsonToDictionary(File.ReadAllText("C:\\Users\\dkurepa\\Desktop\\MOT outputs - Copy\\bAfter.json"));

        var aChanges = FlatJsonComparer.CompareFlatJsons(aBefore, aAfter);
        var bChanges = FlatJsonComparer.CompareFlatJsons(bBefore, bAfter);
        FlatJsonChangeComparer.ApplyChanges(
             File.ReadAllText("C:\\Users\\dkurepa\\Desktop\\MOT outputs - Copy\\aAfter.json"),
             FlatJsonChangeComparer.ComputeChanges(aChanges, bChanges));
        try
        {
            List<Build>? matchingBuilds = null;
            if (_options.Id != 0)
            {
                if (!string.IsNullOrEmpty(_options.Repo) ||
                    !string.IsNullOrEmpty(_options.Commit))
                {
                    Console.WriteLine("--id should not be used with other options.");
                    return Constants.ErrorCode;
                }

                matchingBuilds = [await _barClient.GetBuildAsync(_options.Id)];
            }
            else if (!string.IsNullOrEmpty(_options.Repo) || !string.IsNullOrEmpty(_options.Commit))
            {
                if (string.IsNullOrEmpty(_options.Repo) != string.IsNullOrEmpty(_options.Commit))
                {
                    Console.WriteLine("--repo and --commit should be used together.");
                    return Constants.ErrorCode;
                }
                var subscriptions = await _barClient.GetSubscriptionsAsync();
                var possibleRepos = subscriptions
                    .SelectMany(subscription => new List<string> { subscription.SourceRepository, subscription.TargetRepository })
                    .Where(r => r.Contains(_options.Repo, StringComparison.OrdinalIgnoreCase))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                matchingBuilds = [];
                foreach (string repo in possibleRepos)
                {
                    matchingBuilds.AddRange(await _barClient.GetBuildsAsync(repo, _options.Commit));
                }
                matchingBuilds = matchingBuilds.DistinctBy(build => UxHelpers.GetTextBuildDescription(build)).ToList(); 
            }
            else
            {
                Console.WriteLine("Please specify --id, --uri, or --repo and --commit to lookup a build.");
                return Constants.ErrorCode;
            }

            // Print the build info.
            if (!matchingBuilds.Any())
            {
                Console.WriteLine($"Could not any builds matching the given criteria.");
                return Constants.ErrorCode;
            }

            switch (_options.OutputFormat)
            {
                case DarcOutputType.text:
                    foreach (Build build in matchingBuilds)
                    {
                        Console.Write(UxHelpers.GetTextBuildDescription(build));
                    }
                    break;
                case DarcOutputType.json:
                    object objectToSerialize;
                    if (_options.ExtendedDetails)
                    {
                        objectToSerialize = matchingBuilds;
                    }
                    else
                    {
                        objectToSerialize = matchingBuilds.Select(UxHelpers.GetJsonBuildDescription);
                    }

                    Console.WriteLine(JsonConvert.SerializeObject(objectToSerialize, Formatting.Indented));
                    break;
                default:
                    throw new NotImplementedException($"Output format type {_options.OutputFormat} not yet supported for get-build.");
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
            _logger.LogError(e, "Error: Failed to retrieve build information.");
            return Constants.ErrorCode;
        }
    }
}
