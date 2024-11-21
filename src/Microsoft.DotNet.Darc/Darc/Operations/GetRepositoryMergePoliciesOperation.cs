// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Darc.Operations;

internal class GetRepositoryMergePoliciesOperation : Operation
{
    private readonly GetRepositoryMergePoliciesCommandLineOptions _options;
    private readonly IBarApiClient _barClient;
    private readonly ILogger<GetRepositoryMergePoliciesOperation> _logger;

    public GetRepositoryMergePoliciesOperation(
        GetRepositoryMergePoliciesCommandLineOptions options,
        IBarApiClient barClient,
        ILogger<GetRepositoryMergePoliciesOperation> logger)
    {
        _options = options;
        _barClient = barClient;
        _logger = logger;
    }

    public override async Task<int> ExecuteAsync()
    {
        try
        {
            IEnumerable<RepositoryBranch> allRepositories = await _barClient.GetRepositoriesAsync(null, null);
            IEnumerable<RepositoryBranch> filteredRepositories = allRepositories.Where(repositories =>
                (string.IsNullOrEmpty(_options.Repo) || repositories.Repository.Contains(_options.Repo, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrEmpty(_options.Branch) || repositories.Branch.Contains(_options.Branch, StringComparison.OrdinalIgnoreCase)));

            // List only those repos and branches that are targeted by a batchable subscription (active) unless the user
            // passes --all.
            if (!_options.All)
            {
                HashSet<string> batchableTargets = (await _barClient.GetSubscriptionsAsync())
                    .Where(s => s.Policy.Batchable)
                    .Select(s => $"{s.TargetRepository}{s.TargetBranch}")
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var targetedRepositories = filteredRepositories.Where(r => batchableTargets.Contains($"{r.Repository}{r.Branch}"));

                // If the number of repositories we're about to print is less than what we could have printed, then print a
                // message.
                int difference = filteredRepositories.Count() - targetedRepositories.Count();
                if (difference != 0)
                {
                    Console.WriteLine($"Filtered {difference} policies for branches not targeted by an active batchable subscription. To include, pass --all.{Environment.NewLine}");
                }

                filteredRepositories = targetedRepositories;
            }

            foreach (var repository in filteredRepositories)
            {
                Console.WriteLine($"{repository.Repository} @ {repository.Branch}");
                Console.Write(UxHelpers.GetMergePoliciesDescription(repository.MergePolicies));
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
            _logger.LogError(e, "Error: Failed to retrieve repositories");
            return Constants.ErrorCode;
        }
    }
}
