// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FlatFlowMigrationCli.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;

namespace FlatFlowMigrationCli.Operations;

internal class MigrateRepoOperation : IOperation
{
    private readonly IProductConstructionServiceApi _pcsClient;
    private readonly IGitRepoFactory _gitRepoFactory;
    private readonly ISourceMappingParser _sourceMappingParser;
    private readonly IVersionDetailsParser _versionDetailsParser;
    private readonly VmrDependencyResolver _vmrDependencyResolver;
    private readonly ISubscriptionMigrator _subscriptionMigrator;
    private readonly GitHubClient _gitHubClient;
    private readonly ILogger<MigrateRepoOperation> _logger;

    private readonly MigrateRepoOptions _options;

    public MigrateRepoOperation(
        ILogger<MigrateRepoOperation> logger,
        IProductConstructionServiceApi client,
        IGitRepoFactory gitRepoFactory,
        ISourceMappingParser sourceMappingParser,
        IVersionDetailsParser versionDetailsParser,
        VmrDependencyResolver vmrDependencyResolver,
        ISubscriptionMigrator subscriptionMigrator,
        GitHubClient gitHubClient,
        MigrateRepoOptions options)
    {
        _logger = logger;
        _pcsClient = client;
        _gitRepoFactory = gitRepoFactory;
        _sourceMappingParser = sourceMappingParser;
        _versionDetailsParser = versionDetailsParser;
        _vmrDependencyResolver = vmrDependencyResolver;
        _subscriptionMigrator = subscriptionMigrator;
        _gitHubClient = gitHubClient;
        _options = options;
    }

    public async Task<int> RunAsync()
    {
        IGitRepo vmr = _gitRepoFactory.CreateClient(_options.VmrUri);
        string sourceMappingsJson = await vmr.GetFileContentsAsync(VmrInfo.DefaultRelativeSourceMappingsPath, _options.VmrUri, "main");
        IReadOnlyCollection<SourceMapping> sourceMappings = _sourceMappingParser.ParseMappingsFromJson(sourceMappingsJson);

        SourceMapping mapping = sourceMappings.FirstOrDefault(m => m.Name.Equals(_options.Mapping, StringComparison.InvariantCultureIgnoreCase))
            ?? throw new ArgumentException($"No VMR source mapping named `{_options.Mapping}` found");

        //if (mapping.DisableSynchronization != true)
        //{
        //    _logger.LogWarning("{mapping}'s synchronization from dotnet/sdk is not disabled yet!", mapping.Name);
        //}

        var vmrDependencies = await _vmrDependencyResolver.GetVmrDependencies(_options.VmrUri, Constants.SdkRepoUri, "main");

        try
        {
            var sdkMainSha = await _gitHubClient.GetLastCommitShaAsync(Constants.SdkRepoUri, "main")
                 ?? throw new InvalidOperationException($"Failed to get the latest SHA of the main branch of {Constants.SdkRepoUri}");
            var files = await _gitHubClient.GetFilesAtCommitAsync(Constants.SdkRepoUri, sdkMainSha, Constants.SdkPatchLocation);
            var repoPatchPrefix = $"{Constants.SdkPatchLocation}/{mapping.Name}/";
            if (files.Any(f => f.FilePath.StartsWith(repoPatchPrefix)))
            {
                throw new InvalidOperationException($"Repository {mapping.Name} still has source build patches in dotnet/sdk!");
            }
        }
        catch (Exception e) when (e.Message.Contains("could not be found"))
        {
            // No patches left in dotnet/sdk
        }

        var branch = mapping.DefaultRef;
        var repoUri = mapping.DefaultRemote;

        var defaultChannels = await _pcsClient.DefaultChannels.ListAsync(branch, repository: repoUri);
        if (defaultChannels?.Count != 1)
        {
            throw new ArgumentException($"Expected exactly one default channel for {branch} of {repoUri}, found {defaultChannels?.Count()}");
        }

        var channel = defaultChannels.First().Channel;

        _logger.LogInformation("Migrating branch {branch} of {repoUri} to flat flow...", branch, repoUri);

        List<Subscription> codeFlowSubscriptions =
        [
            .. await _pcsClient.Subscriptions.ListSubscriptionsAsync(sourceRepository: repoUri, channelId: channel.Id, sourceEnabled: true),
            .. (await _pcsClient.Subscriptions.ListSubscriptionsAsync(targetRepository: repoUri, sourceEnabled: true))
                .Where(s => s.TargetBranch == branch),
        ];

        if (codeFlowSubscriptions.Count > 0)
        {
            throw new ArgumentException($"Found existing code flow subscriptions for {repoUri} / {branch}");
        }

        List<Subscription> outgoingSubscriptions = await _pcsClient.Subscriptions.ListSubscriptionsAsync(
            enabled: true,
            sourceRepository: repoUri,
            channelId: channel.Id,
            sourceEnabled: false);

        List<Subscription> incomingSubscriptions = (await _pcsClient.Subscriptions.ListSubscriptionsAsync(
                enabled: true,
                targetRepository: repoUri,
                sourceEnabled: false))
            .Where(s => s.TargetBranch == branch)
            .ToList();

        _logger.LogInformation("Found {outgoing} outgoing and {incoming} incoming subscriptions for {repo}",
            outgoingSubscriptions.Count,
            incomingSubscriptions.Count,
            _options.Mapping);

        var arcadeSubscription = incomingSubscriptions
            .FirstOrDefault(s => s.SourceRepository == Constants.ArcadeRepoUri);

        HashSet<string> excludedAssets = [];
        if (arcadeSubscription != null && arcadeSubscription.Channel.Name != Constants.LatestArcadeChannel)
        {
            excludedAssets.Add(DependencyFileManager.ArcadeSdkPackageName);

            var repo = _gitRepoFactory.CreateClient(repoUri);
            var versionDetailsContents = await repo.GetFileContentsAsync(VersionFiles.VersionDetailsXml, repoUri, branch);
            var versionDetails = _versionDetailsParser.ParseVersionDetailsXml(versionDetailsContents);

            foreach (var dependency in versionDetails.Dependencies)
            {
                if (dependency.RepoUri == Constants.ArcadeRepoUri)
                {
                    excludedAssets.Add(dependency.Name);
                }
            }

            _logger.LogInformation("Arcade subscription is for {channel} channel. Excluding {count} Arcade assets in backflow subscription",
                arcadeSubscription.Channel.Name,
                excludedAssets.Count);
        }

        var vmrChannel = (await _pcsClient.Channels.ListChannelsAsync())
            .First(c => c.Name == Constants.VmrChannelName);

        foreach (var incoming in incomingSubscriptions)
        {
            _logger.LogInformation("Processing incoming subscription {subscriptionId} {sourceRepository} -> {targetRepository}...",
                incoming.Id,
                incoming.SourceRepository,
                incoming.TargetRepository);

            if (!sourceMappings.Any(m => m.DefaultRemote == incoming.SourceRepository))
            {
                // Not a VMR repository
                _logger.LogInformation("{sourceRepository} is not a VMR repository, skipping...", incoming.SourceRepository);
                continue;
            }

            if (incoming.SourceRepository == Constants.VmrUri)
            {
                await _subscriptionMigrator.DeleteSubscription(incoming);
                continue;
            }

            await _subscriptionMigrator.DisableSubscription(incoming);
        }

        foreach (var outgoing in outgoingSubscriptions)
        {
            _logger.LogInformation("Processing outgoing subscription {subscriptionId} {sourceRepository} -> {targetRepository}...",
                outgoing.Id,
                outgoing.SourceRepository,
                outgoing.TargetRepository);

            await _subscriptionMigrator.DisableSubscription(outgoing);

            // VMR repositories will already have a VMR subscription
            if (sourceMappings.Any(m => m.DefaultRemote == outgoing.TargetRepository))
            {
                _logger.LogInformation("Not recreating a VMR subscription for {repoUri} as it's a VMR repository",
                    outgoing.TargetRepository);
                continue;
            }

            var existingVmrSubscriptions = await _pcsClient.Subscriptions.ListSubscriptionsAsync(
                sourceRepository: Constants.VmrUri,
                channelId: vmrChannel.Id,
                targetRepository: outgoing.TargetRepository);

            if (existingVmrSubscriptions.Count > 0)
            {
                _logger.LogInformation("Not recreating a VMR subscription for {repoUri} as it's already subscribed to the VMR",
                    outgoing.TargetRepository);
                continue;
            }

            await _subscriptionMigrator.CreateVmrSubscription(outgoing);
        }

        await _subscriptionMigrator.CreateBackflowSubscription(mapping.Name, repoUri, branch, excludedAssets);

        _logger.LogInformation("Repository {mapping} successfully migrated", mapping.Name);

        return 0;
    }
}
