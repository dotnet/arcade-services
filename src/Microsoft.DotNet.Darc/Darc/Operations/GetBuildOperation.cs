// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations;

internal class GetBuildOperation : Operation
{
    private readonly GetBuildCommandLineOptions _options;

    public GetBuildOperation(GetBuildCommandLineOptions options, IServiceCollection? services = null)
        : base(options, services)
    {
        _options = options;
    }

    /// <summary>
    ///     Get a specific build of a repository
    /// </summary>
    /// <returns>Process exit code.</returns>
    public override async Task<int> ExecuteAsync()
    {
        try
        {
            IBarApiClient barClient = Provider.GetRequiredService<IBarApiClient>();

            List<Build>? matchingBuilds = null;
            if (_options.Id != 0)
            {
                if (!string.IsNullOrEmpty(_options.Repo) ||
                    !string.IsNullOrEmpty(_options.Commit))
                {
                    Console.WriteLine("--id should not be used with other options.");
                    return Constants.ErrorCode;
                }

                matchingBuilds = [await barClient.GetBuildAsync(_options.Id)];
            }
            else if (!string.IsNullOrEmpty(_options.Repo) || !string.IsNullOrEmpty(_options.Commit))
            {
                if (string.IsNullOrEmpty(_options.Repo) != string.IsNullOrEmpty(_options.Commit))
                {
                    Console.WriteLine("--repo and --commit should be used together.");
                    return Constants.ErrorCode;
                }
                var subscriptions = await barClient.GetSubscriptionsAsync();
                var possibleRepos = subscriptions
                    .SelectMany(subscription => new List<string> { subscription.SourceRepository, subscription.TargetRepository })
                    .Where(r => r.Contains(_options.Repo, StringComparison.OrdinalIgnoreCase))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                matchingBuilds = [];
                foreach (string repo in possibleRepos)
                {
                    matchingBuilds.AddRange(await barClient.GetBuildsAsync(repo, _options.Commit));
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
            Logger.LogError(e, "Error: Failed to retrieve build information.");
            return Constants.ErrorCode;
        }
    }

    protected override bool IsOutputFormatSupported(DarcOutputType outputFormat)
        => outputFormat switch
        {
            DarcOutputType.json => true,
            _ => base.IsOutputFormatSupported(outputFormat),
        };
}
