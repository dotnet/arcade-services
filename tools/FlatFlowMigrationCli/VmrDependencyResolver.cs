// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FlatFlowMigrationCli.Operations;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;

namespace FlatFlowMigrationCli;

internal record VmrDependency(SourceMapping Mapping, DefaultChannel Channel);

internal class VmrDependencyResolver
{
    private readonly IProductConstructionServiceApi _pcsClient;
    private readonly IGitRepoFactory _gitRepoFactory;
    private readonly ISourceMappingParser _sourceMappingParser;
    private readonly ILogger<MigrateRepoOperation> _logger;

    public VmrDependencyResolver(
        IProductConstructionServiceApi pcsClient,
        IGitRepoFactory gitRepoFactory,
        ISourceMappingParser sourceMappingParser,
        ILogger<MigrateRepoOperation> logger)
    {
        _pcsClient = pcsClient;
        _logger = logger;
        _gitRepoFactory = gitRepoFactory;
        _sourceMappingParser = sourceMappingParser;
    }

    public async Task<List<VmrDependency>> GetVmrDependencies(string vmrUri, string rootRepoUri, string branch)
    {
        IGitRepo vmr = _gitRepoFactory.CreateClient(vmrUri);
        var sourceMappingsJson = await vmr.GetFileContentsAsync(VmrInfo.DefaultRelativeSourceMappingsPath, vmrUri, "main");
        IReadOnlyCollection<SourceMapping> sourceMappings = _sourceMappingParser.ParseMappingsFromJson(sourceMappingsJson);

        DefaultChannel sdkChannel = (await _pcsClient.DefaultChannels.ListAsync(repository: rootRepoUri, branch: branch))
            .Single();

        var repositories = new Queue<VmrDependency>(
        [
            new VmrDependency(sourceMappings.First(m => m.Name == "sdk"), sdkChannel)
        ]);

        var dependencies = new List<VmrDependency>();

        _logger.LogInformation("Analyzing the dependency tree of repositories flowing to VMR...");

        while (repositories.TryDequeue(out var node))
        {
            _logger.LogInformation("  {mapping} / {branch} / {channel}",
                node.Mapping.Name,
                node.Channel.Branch,
                node.Channel.Channel.Name);
            dependencies.Add(node);

            var incomingSubscriptions = (await _pcsClient.Subscriptions
                .ListSubscriptionsAsync(targetRepository: node.Channel.Repository, enabled: true))
                .Where(s => s.TargetBranch == node.Channel.Branch)
                .ToList();

            // Check all subscriptions going to the current repository
            foreach (var incoming in incomingSubscriptions)
            {
                var mapping = sourceMappings.FirstOrDefault(m => m.DefaultRemote.Equals(incoming.SourceRepository, StringComparison.InvariantCultureIgnoreCase));
                if (mapping == null)
                {
                    // VMR repos only
                    continue;
                }

                if (dependencies.Any(n => n.Mapping.Name == mapping.Name) || repositories.Any(r => r.Mapping.Name == mapping.Name))
                {
                    // Already processed
                    continue;
                }

                if (incoming.SourceRepository == Constants.ArcadeRepoUri)
                {
                    // Arcade will be handled separately
                    // It also publishes to the validation channel so the look-up below won't work
                    continue;
                }

                // Find which branch publishes to the incoming subscription
                List<DefaultChannel> defaultChannels = await _pcsClient.DefaultChannels.ListAsync(repository: incoming.SourceRepository);
                var matchingChannels = defaultChannels
                    .Where(c => c.Channel.Id == incoming.Channel.Id)
                    .ToList();
                DefaultChannel defaultChannel;

                switch (matchingChannels.Count)
                {
                    case 0:
                        _logger.LogWarning(
                            "  No {dependency} branch publishing to channel '{channel}' for dependency of {parent}. " +
                            "Using default branch {ref}",
                            mapping.Name,
                            incoming.Channel.Name,
                            node.Mapping.Name,
                            mapping.DefaultRef);
                        defaultChannel = new DefaultChannel(0, incoming.SourceRepository, true)
                        {
                            Branch = mapping.DefaultRef,
                            Channel = incoming.Channel,
                        };
                        break;

                    case 1:
                        defaultChannel = matchingChannels.Single();
                        break;

                    default:
                        if (matchingChannels.Any(c => c.Branch == mapping.DefaultRef))
                        {
                            defaultChannel = matchingChannels.Single(c => c.Branch == mapping.DefaultRef);
                            _logger.LogWarning(
                                "  Multiple {repo} branches publishing to channel '{channel}' for dependency of {parent}. " +
                                "Using the one that matches the default branch {ref}",
                                mapping.Name,
                                incoming.Channel.Name,
                                node.Mapping.Name,
                                mapping.DefaultRef);
                        }
                        else
                        {
                            defaultChannel = matchingChannels.First();
                            _logger.LogWarning(
                                "  Multiple {dependency} branches publishing to channel '{channel}' for dependency of {parent}. " +
                                "Using the first one",
                                mapping.Name,
                                incoming.Channel.Name,
                                node.Mapping.Name);
                        }

                        break;
                }

                repositories.Enqueue(new VmrDependency(mapping, defaultChannel));
            }
        }

        _logger.LogInformation("Found {count} repositories flowing to VMR", dependencies.Count);
        foreach (var missing in sourceMappings.Where(m => !dependencies.Any(d => d.Mapping.Name == m.Name)))
        {
            _logger.LogWarning("Repository {mapping} not found in the dependency tree", missing.Name);
        }

        return dependencies;
    }
}
