// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.Logging;
using ProductConstructionService.DependencyFlow.WorkItems;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.DependencyFlow.WorkItemProcessors;

public class BuildCoherencyInfoProcessor : WorkItemProcessor<BuildCoherencyInfoWorkItem>
{
    private readonly BuildAssetRegistryContext _context;
    private readonly IRemoteFactory _remoteFactory;
    private readonly IBasicBarClient _barClient;
    private readonly ILogger<BuildCoherencyInfoWorkItem> _logger;

    public BuildCoherencyInfoProcessor(
        BuildAssetRegistryContext context,
        IRemoteFactory remoteFactory,
        IBasicBarClient barClient,
        ILogger<BuildCoherencyInfoWorkItem> logger)
    {
        _context = context;
        _remoteFactory = remoteFactory;
        _barClient = barClient;
        _logger = logger;
    }

    /// <summary>
    /// This method is called asynchronously whenever a new build is inserted in BAR.
    /// Its goal is to compute the incoherent dependencies that the build have
    /// and persist the list of them in BAR.
    /// </summary>
    public override async Task<bool> ProcessWorkItemAsync(
        BuildCoherencyInfoWorkItem workItem,
        CancellationToken cancellationToken)
    {
        var graphBuildOptions = new DependencyGraphBuildOptions()
        {
            IncludeToolset = false,
            LookupBuilds = false,
            NodeDiff = NodeDiff.None
        };

        try
        {
            Maestro.Data.Models.Build? build = await _context.Builds.FindAsync([workItem.BuildId], cancellationToken);

            if (build == null)
            {
                _logger.LogError("Build {buildId} not found", workItem.BuildId);
                return false;
            }

            DependencyGraph graph = await DependencyGraph.BuildRemoteDependencyGraphAsync(
                _remoteFactory,
                _barClient,
                build.GetRepository(),
                build.Commit,
                graphBuildOptions,
                _logger);

            var incoherencies = graph.IncoherentDependencies
                .Select(incoherence => new Maestro.Data.Models.BuildIncoherence
                {
                    Name = incoherence.Name,
                    Version = incoherence.Version,
                    Repository = incoherence.RepoUri,
                    Commit = incoherence.Commit
                })
                .ToList();

            _context.Entry(build).Reload();
            build.Incoherencies = incoherencies;
            _context.Builds.Update(build);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, $"Problems computing the dependency incoherencies for BAR build {workItem.BuildId}");
            return false;
        }

        return true;
    }

    protected override Dictionary<string, object> GetLoggingContextData(BuildCoherencyInfoWorkItem workItem)
    {
        var data = base.GetLoggingContextData(workItem);
        data["BuildId"] = workItem.BuildId;
        return data;
    }
}
