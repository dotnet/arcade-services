// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.Core.WebApi;

namespace BuildInsights.BuildAnalysis;

public interface IBuildOperationsService
{
    Task<bool> RetryBuild(string orgId, string projectId, int buildId, CancellationToken cancellationToken);
}

public sealed class BuildOperationsProvider : IBuildOperationsService
{
    private readonly VssConnectionProvider _connection;
    private readonly ILogger<BuildOperationsProvider> _logger;

    public BuildOperationsProvider(
        VssConnectionProvider connection,
        ILogger<BuildOperationsProvider> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    public async Task<bool> RetryBuild(string orgId, string projectId, int buildId, CancellationToken cancellationToken)
    {
        using var connection = _connection.GetConnection(orgId);
        BuildHttpClient buildClient = connection.Value.GetClient<BuildHttpClient>();
        Build build = await buildClient.GetBuildAsync(
            projectId,
            buildId,
            cancellationToken: cancellationToken
        );

        //In order to perform a retry the build body information must be 'empty', creating a Build with the basic information
        var shallowBuild = new Build
        {
            Id = buildId,
            Project = new TeamProjectReference
            {
                Id = build.Project.Id
            }
        };

        try
        {
            await buildClient.UpdateBuildAsync(shallowBuild, retry: true, cancellationToken: cancellationToken);
            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e, $"Unable to retry build {buildId}");
            return false;
        }
    }
}
