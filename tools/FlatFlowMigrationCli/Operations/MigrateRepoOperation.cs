// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FlatFlowMigrationCli.Options;
using Maestro.Common;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FlatFlowMigrationCli.Operations;

internal class MigrateRepoOperation : IOperation
{
    private const string VmrUri = "https://github.com/dotnet/dotnet";
    private const string ArcadeRepoUri = "https://github.com/dotnet/arcade";
    private const string SdkRepoUri = "https://github.com/dotnet/sdk";
    private const string LatestArcadeChannel = ".NET Eng - Latest";
    private const string VmrChannelName = ".NET 10 UB";
    private const string SdkPatchLocation = "src/SourceBuild/patches";

    private readonly IProductConstructionServiceApi _pcsClient;
    private readonly IGitRepoFactory _gitRepoFactory;
    private readonly ISourceMappingParser _sourceMappingParser;
    private readonly IVersionDetailsParser _versionDetailsParser;
    private readonly VmrDependencyResolver _vmrDependencyResolver;
    private readonly ILogger<MigrateRepoOperation> _logger;

    private readonly MigrateRepoOptions _options;

    public MigrateRepoOperation(
        ILogger<MigrateRepoOperation> logger,
        IProductConstructionServiceApi client,
        IGitRepoFactory gitRepoFactory,
        ISourceMappingParser sourceMappingParser,
        IVersionDetailsParser versionDetailsParser,
        VmrDependencyResolver vmrDependencyResolver,
        MigrateRepoOptions options)
    {
        _logger = logger;
        _pcsClient = client;
        _gitRepoFactory = gitRepoFactory;
        _sourceMappingParser = sourceMappingParser;
        _versionDetailsParser = versionDetailsParser;
        _vmrDependencyResolver = vmrDependencyResolver;
        _options = options;
    }

    public async Task<int> RunAsync()
    {
        var vmrChannel = (await _pcsClient.Channels.ListChannelsAsync())
            .First(c => c.Name == VmrChannelName);

        IGitRepo vmr = _gitRepoFactory.CreateClient(_options.VmrUri);
        string sourceMappingsJson = await vmr.GetFileContentsAsync(VmrInfo.DefaultRelativeSourceMappingsPath, _options.VmrUri, "main");
        IReadOnlyCollection<SourceMapping> sourceMappings = _sourceMappingParser.ParseMappingsFromJson(sourceMappingsJson);

        SourceMapping mapping = sourceMappings.FirstOrDefault(m => m.Name.Equals(_options.Mapping, StringComparison.InvariantCultureIgnoreCase))
            ?? throw new ArgumentException($"No VMR source mapping named `{_options.Mapping}` found");

        //if (mapping.DisableSynchronization != true)
        //{
        //    _logger.LogWarning("{mapping}'s synchronization from dotnet/sdk is not disabled yet!", mapping.Name);
        //}

        var vmrDependencies = await _vmrDependencyResolver.GetVmrDependencies(_options.VmrUri, SdkRepoUri, "main");

        // TODO: Use DI
        IConfiguration userSecrets = new ConfigurationBuilder()
            .AddUserSecrets<MigrateRepoOperation>()
            .Build();
        var gitHubToken = userSecrets["GITHUB_TOKEN"] ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        var gitHubClient = new GitHubClient(
            new ResolvedTokenProvider(gitHubToken),
            new ProcessManager(_logger, "git"),
            _logger,
            null);

        try
        {
            var sdkMainSha = await gitHubClient.GetLastCommitShaAsync(SdkRepoUri, "main")
                 ?? throw new InvalidOperationException($"Failed to get the latest SHA of the main branch of {SdkRepoUri}");
            var files = await gitHubClient.GetFilesAtCommitAsync(SdkRepoUri, sdkMainSha, SdkPatchLocation);
            var repoPatchPrefix = $"{SdkPatchLocation}/{mapping.Name}/";
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

        List<Subscription> incomingSubscriptions = (await _pcsClient.Subscriptions
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

        var arcadeSubscription = incomingSubscriptions
            .FirstOrDefault(s => s.SourceRepository == ArcadeRepoUri);

        HashSet<string> excludedAssets = [];
        if (arcadeSubscription != null && arcadeSubscription.Channel.Name != LatestArcadeChannel)
        {
            excludedAssets.Add(DependencyFileManager.ArcadeSdkPackageName);

            var repo = _gitRepoFactory.CreateClient(repoUri);
            var versionDetailsContents = await repo.GetFileContentsAsync(VersionFiles.VersionDetailsXml, repoUri, branch);
            var versionDetails = _versionDetailsParser.ParseVersionDetailsXml(versionDetailsContents);

            foreach (var dependency in versionDetails.Dependencies)
            {
                if (dependency.RepoUri == ArcadeRepoUri)
                {
                    excludedAssets.Add(dependency.Name);
                }
            }

            _logger.LogInformation("Arcade subscription is for {channel} channel. Excluding {count} Arcade assets in backflow subscription",
                arcadeSubscription.Channel.Name,
                excludedAssets.Count);
        }

        foreach (var incoming in incomingSubscriptions)
        {
            _logger.LogInformation("Processing incoming subscription {subscriptionId} from {sourceRepository}...",
                incoming.Id,
                incoming.SourceRepository);

            if (!sourceMappings.Any(m => m.DefaultRemote == incoming.SourceRepository))
            {
                // Not a VMR repository
                _logger.LogInformation("{sourceRepository} is not a VMR repository, skipping...", incoming.SourceRepository);
                continue;
            }

            if (incoming.SourceRepository == VmrUri)
            {
                _logger.LogInformation("Deleting an existing subscription to VMR {subscriptionId}...", incoming.Id);
                //await _pcsClient.Subscriptions.DeleteSubscriptionAsync(incoming.Id);
                continue;
            }

            var disabledSubscription = new SubscriptionUpdate
            {
                ChannelName = incoming.Channel.Name,
                SourceRepository = incoming.SourceRepository,
                Enabled = false,
                Policy = incoming.Policy,
                PullRequestFailureNotificationTags = incoming.PullRequestFailureNotificationTags,
            };

            //await _pcsClient.Subscriptions.UpdateSubscriptionAsync(incoming.Id, disabledSubscription);
            _logger.LogInformation("Disabled incoming subscription {subscriptionId} from {sourceRepository}",
                incoming.Id,
                incoming.SourceRepository);
        }

        foreach (var outgoing in outgoingSubscriptions)
        {
            _logger.LogInformation("Processing outgoing subscription {subscriptionId} to {targetRepository}...",
                outgoing.Id,
                outgoing.TargetRepository);

            var disabledSubscription = new SubscriptionUpdate
            {
                ChannelName = outgoing.Channel.Name,
                SourceRepository = outgoing.SourceRepository,
                Enabled = false,
                Policy = outgoing.Policy,
                PullRequestFailureNotificationTags = outgoing.PullRequestFailureNotificationTags,
            };

            //await _pcsClient.Subscriptions.UpdateSubscriptionAsync(outgoing.Id, disabledSubscription);
            _logger.LogInformation("Disabled outgoing subscription {subscriptionId} to {targetRepository}...",
                outgoing.Id,
                outgoing.TargetRepository);

            // VMR repositories will already have a VMR subscription
            if (sourceMappings.Any(m => m.DefaultRemote == outgoing.TargetRepository))
            {
                _logger.LogInformation("Not recreating a VMR subscription for {repoUri} as it's a VMR repository",
                    outgoing.TargetRepository);
                continue;
            }

            var existingVmrSubscriptions = await _pcsClient.Subscriptions.ListSubscriptionsAsync(
                sourceRepository: VmrUri,
                channelId: vmrChannel.Id,
                targetRepository: outgoing.TargetRepository);

            if (existingVmrSubscriptions.Count > 0)
            {
                _logger.LogInformation("Not recreating a VMR subscription for {repoUri} as it's already subscribed to the VMR",
                    outgoing.TargetRepository);
                continue;
            }

            _logger.LogInformation("Creating a new VMR subscription for {repoUri}...", outgoing.TargetRepository);

            var newVmrSubscription = new SubscriptionData(
                VmrChannelName,
                VmrUri,
                outgoing.TargetRepository,
                outgoing.TargetBranch,
                outgoing.Policy,
                outgoing.PullRequestFailureNotificationTags);

            //var newSub = await _pcsClient.Subscriptions.CreateAsync(newVmrSubscription);
            //_logger.LogInformation("Created subscription {subscriptionId} for {repoUri} from the VMR", newSub.Id, outgoing.TargetRepository);
        }

        // Create a VMR backflow subscription
        _logger.LogInformation("Creating a backflow subscription for {repoUri}", repoUri);
        //await _pcsClient.Subscriptions.CreateAsync(new SubscriptionData(
        //    VmrChannel,
        //    VmrUri,
        //    repoUri,
        //    branch,
        //    new SubscriptionPolicy(batchable: false, UpdateFrequency.EveryBuild)
        //    {
        //        MergePolicies = [ new MergePolicy() { Name = "Standard" } ]
        //    },
        //    null)
        //{
        //    SourceEnabled = true,
        //    SourceDirectory = mapping.Name,
        //    ExcludedAssets = [..excludedAssets],
        //});

        _logger.LogInformation("Repository {mapping} successfully migrated", mapping.Name);

        return 0;
    }
}
