// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FlatFlowMigrationCli.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;

namespace FlatFlowMigrationCli.Operations;

internal class MigrateRepoOperation : IOperation
{
    private readonly IProductConstructionServiceApi _client;
    private readonly IGitRepoFactory _gitRepoFactory;
    private readonly ISourceMappingParser _sourceMappingParser;
    private readonly MigrateRepoOptions _options;
    private readonly ILogger<MigrateRepoOperation> _logger;

    public MigrateRepoOperation(
        ILogger<MigrateRepoOperation> logger,
        IProductConstructionServiceApi client,
        IGitRepoFactory gitRepoFactory,
        ISourceMappingParser sourceMappingParser,
        MigrateRepoOptions options)
    {
        _logger = logger;
        _client = client;
        _gitRepoFactory = gitRepoFactory;
        _sourceMappingParser = sourceMappingParser;
        _options = options;
    }

    public async Task<int> RunAsync()
    {
        IGitRepo vmr = _gitRepoFactory.CreateClient(_options.VmrUri);
        string sourceMappingsJson = await vmr.GetFileContentsAsync(VmrInfo.DefaultRelativeSourceMappingsPath, _options.VmrUri, "main");
        IReadOnlyCollection<SourceMapping> sourceMappings = _sourceMappingParser.ParseMappingsFromJson(sourceMappingsJson);

        SourceMapping mapping = sourceMappings.FirstOrDefault(m => m.Name.Equals(_options.Mapping, StringComparison.InvariantCultureIgnoreCase))
            ?? throw new ArgumentException($"No VMR source mapping named `{_options.Mapping}` found");

        if (mapping.DisableSynchronization != true)
        {
            throw new ArgumentException($"{_options.Mapping}'s synchronization from dotnet/sdk is not disabled yet!");
        }

        var branch = mapping.DefaultRef;
        var repoUri = mapping.DefaultRemote;

        var defaultChannels = await _client.DefaultChannels.ListAsync(branch, repository: repoUri);
        if (defaultChannels?.Count != 1)
        {
            throw new ArgumentException($"Expected exactly one default channel for {branch} of {repoUri}, found {defaultChannels?.Count()}");
        }

        var channel = defaultChannels.First();

        _logger.LogInformation("Migrating branch {branch} of {repoUri} to flat flow...", branch, repoUri);

        List<Subscription> codeFlowSubscriptions =
        [
            .. await _client.Subscriptions.ListSubscriptionsAsync(sourceRepository: repoUri, channelId: channel.Id, sourceEnabled: true),
            .. (await _client.Subscriptions.ListSubscriptionsAsync(targetRepository: repoUri, sourceEnabled: true))
                .Where(s => s.TargetBranch == branch),
        ];

        if (codeFlowSubscriptions.Count > 0)
        {
            throw new ArgumentException($"Found existing code flow subscriptions for {repoUri} / {branch}");
        }

        List<Subscription> outgoingSubscriptions = await _client.Subscriptions.ListSubscriptionsAsync(
            enabled: true,
            sourceRepository: repoUri,
            channelId: defaultChannels[0].Id,
            sourceEnabled: false);

        List<Subscription> incomingSubscriptions = (await _client.Subscriptions
            .ListSubscriptionsAsync(
                enabled: true,
                targetRepository: repoUri,
                sourceEnabled: false))
            .Where(s => s.TargetBranch == branch)
            .ToList();

        _logger.LogInformation("Found {outgoing} outgoing and {incoming} incoming subscriptions for {repo}",
            outgoingSubscriptions.Count,
            incomingSubscriptions.Count,
            _options.Mapping);

        return 0;
    }
}
