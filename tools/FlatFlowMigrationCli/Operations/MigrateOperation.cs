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
using Tools.Common;
using Constants = Tools.Common.Constants;

namespace FlatFlowMigrationCli.Operations;

internal class MigrateOperation : IOperation
{
    internal static readonly string[] ReposWithOwnOfficialBuild =
    [
        "https:/github.com/dotnet/arcade",
        "https:/github.com/dotnet/aspire",
        "https:/github.com/dotnet/command-line-api",
        "https:/github.com/dotnet/deployment-tools",
        "https:/github.com/dotnet/fsharp",
        "https:/github.com/nuget/nuget.client",
        "https:/github.com/dotnet/msbuild",
        "https:/github.com/dotnet/roslyn",
        "https:/github.com/dotnet/vstest",
        "https:/github.com/dotnet/xdt",

        // TODO https://github.com/dotnet/source-build/issues/3737: Final list to be determined
        // "https:/github.com/dotnet/cecil",
        // "https:/github.com/dotnet/diagnstics",
        // "https:/github.com/dotnet/razor",
        // "https:/github.com/dotnet/sourcelink",
        // "https:/github.com/dotnet/symreader",
        // "https:/github.com/dotnet/roslyn-analyzers",
    ];

    private readonly IProductConstructionServiceApi _pcsClient;
    private readonly IGitRepoFactory _gitRepoFactory;
    private readonly ISourceMappingParser _sourceMappingParser;
    private readonly IVersionDetailsParser _versionDetailsParser;
    private readonly VmrDependencyResolver _vmrDependencyResolver;
    private readonly ISubscriptionMigrator _subscriptionMigrator;
    private readonly GitHubClient _gitHubClient;
    private readonly ILogger<MigrateOperation> _logger;

    private readonly MigrateOptions _options;

    public MigrateOperation(
        ILogger<MigrateOperation> logger,
        IProductConstructionServiceApi client,
        IGitRepoFactory gitRepoFactory,
        ISourceMappingParser sourceMappingParser,
        IVersionDetailsParser versionDetailsParser,
        VmrDependencyResolver vmrDependencyResolver,
        ISubscriptionMigrator subscriptionMigrator,
        GitHubClient gitHubClient,
        MigrateOptions options)
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
        if (_options.PerformUpdates)
        {
            Console.Write("This is not a dry run, changes to subscriptions will be made. Continue (y/N)? ");
            var key = Console.ReadKey(intercept: false);
            Console.WriteLine();

            if (key.KeyChar != 'y' && key.KeyChar != 'Y')
            {
                _logger.LogInformation("Operation cancelled by user.");
                return 1;
            }
        }

        IGitRepo vmr = _gitRepoFactory.CreateClient(_options.VmrUri);
        string sourceMappingsJson = await vmr.GetFileContentsAsync(VmrInfo.DefaultRelativeSourceMappingsPath, _options.VmrUri, "main");
        IReadOnlyCollection<SourceMapping> sourceMappings = _sourceMappingParser.ParseMappingsFromJson(sourceMappingsJson);

        List<VmrRepository> vmrRepositories = await _vmrDependencyResolver.GetVmrRepositoriesAsync(
            _options.VmrUri,
            Constants.SdkRepoUri,
            "main");

        if (_options.Repositories.Any())
        {
            vmrRepositories = vmrRepositories
                .Where(r => _options.Repositories.Contains(r.Mapping.Name, StringComparer.OrdinalIgnoreCase))
                .ToList();
            if (vmrRepositories.Count == 0)
            {
                _logger.LogWarning("No repositories found matching the specified names");
                return 1;
            }
        }

        foreach (var repository in vmrRepositories)
        {
            await VerifyNoPatchesLeft(repository);
        }

        foreach (var repository in vmrRepositories)
        {
            await MigrateRepositoryToFlatFlow(sourceMappings, repository);
        }

        return 0;
    }

    /// <summary>
    /// Redirects subscription around a given repository so that it flows directly to/from the VMR.
    /// </summary>
    private async Task MigrateRepositoryToFlatFlow(IReadOnlyCollection<SourceMapping> sourceMappings, VmrRepository repository)
    {
        var branch = repository.Mapping.DefaultRef;
        var repoUri = repository.Mapping.DefaultRemote;

        _logger.LogInformation("Migrating branch {branch} of {repoUri} to flat flow...", branch, repoUri);

        List<Subscription> codeFlowSubscriptions =
        [
            .. await _pcsClient.Subscriptions.ListSubscriptionsAsync(sourceRepository: repoUri, channelId: repository.Channel.Channel.Id, sourceEnabled: true),
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
            channelId: repository.Channel.Channel.Id,
            sourceEnabled: false);

        List<Subscription> incomingSubscriptions = [..(await _pcsClient.Subscriptions.ListSubscriptionsAsync(
                enabled: true,
                targetRepository: repoUri,
                sourceEnabled: false))
            .Where(s => s.TargetBranch == branch)];

        _logger.LogInformation("Found {outgoing} outgoing and {incoming} incoming subscriptions for {repo}",
            outgoingSubscriptions.Count,
            incomingSubscriptions.Count,
            repository.Mapping.Name);

        await MigrateIncomingSubscriptions(sourceMappings, incomingSubscriptions);
        await MigrateOutgoingSubscriptions(sourceMappings, repoUri, outgoingSubscriptions);

        var arcadeSubscription = incomingSubscriptions
            .FirstOrDefault(s => s.SourceRepository == Constants.ArcadeRepoUri);

        // If we depend on older Arcade, we want to exclude it during backflows
        HashSet<string> excludedAssets = arcadeSubscription != null && arcadeSubscription.Channel.Name != Constants.LatestArcadeChannel
            ? await GetArcadePackagesToExclude(branch, repoUri, arcadeSubscription)
            : [];

        await _subscriptionMigrator.CreateBackflowSubscriptionAsync(repository.Mapping.Name, repoUri, branch, excludedAssets);
        await _subscriptionMigrator.CreateForwardFlowSubscriptionAsync(repository.Mapping.Name, repoUri, repository.Channel.Channel.Name);

        if (_options.PerformUpdates)
        {
            _logger.LogInformation("Repository {mapping} successfully migrated", repository.Mapping.Name);
        }
    }

    private async Task VerifyNoPatchesLeft(VmrRepository dependency)
    {
        try
        {
            var sdkMainSha = await _gitHubClient.GetLastCommitShaAsync(Constants.SdkRepoUri, "main")
                 ?? throw new InvalidOperationException($"Failed to get the latest SHA of the main branch of {Constants.SdkRepoUri}");
            var files = await _gitHubClient.GetFilesAtCommitAsync(Constants.SdkRepoUri, sdkMainSha, Constants.SdkPatchLocation);
            var repoPatchPrefix = $"{Constants.SdkPatchLocation}/{dependency.Mapping.Name}/";

            if (files.Any(f => f.FilePath.StartsWith(repoPatchPrefix)))
            {
                throw new InvalidOperationException($"Repository {dependency.Mapping.Name} still has source build patches in dotnet/sdk!");
            }
        }
        catch (Exception e) when (e.Message.Contains("could not be found"))
        {
            // No patches left in dotnet/sdk
        }
    }

    /// <summary>
    /// Migrates subscriptions originating in the currently migrated repository.
    /// </summary>
    private async Task MigrateOutgoingSubscriptions(
        IReadOnlyCollection<SourceMapping> sourceMappings,
        string repoUri,
        List<Subscription> outgoingSubscriptions)
    {
        var vmrChannel = (await _pcsClient.Channels.ListChannelsAsync())
            .First(c => c.Name == Constants.VmrChannelName);

        foreach (var outgoing in outgoingSubscriptions)
        {
            _logger.LogInformation("Processing outgoing subscription {subscriptionId} {sourceRepository} -> {targetRepository}...",
                outgoing.Id,
                outgoing.SourceRepository,
                outgoing.TargetRepository);

            var targetIsInVmr = sourceMappings.Any(m => m.DefaultRemote == outgoing.TargetRepository);
            if (!targetIsInVmr && IsVmrRepoWithOwnOfficialBuild(repoUri))
            {
                _logger.LogInformation("Skipping subscription {subscriptionId} as parent repo has own official build and dependents will stay subscribed there",
                    outgoing.Id);
                continue;
            }

            await _subscriptionMigrator.DisableSubscriptionAsync(outgoing);

            // VMR repositories will already have a VMR subscription
            if (targetIsInVmr)
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

            await _subscriptionMigrator.CreateVmrSubscriptionAsync(outgoing);
        }
    }

    /// <summary>
    /// Migrates subscriptions ending in the currently migrated repository.
    /// </summary>
    private async Task MigrateIncomingSubscriptions(IReadOnlyCollection<SourceMapping> sourceMappings, List<Subscription> incomingSubscriptions)
    {
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
                await _subscriptionMigrator.DeleteSubscriptionAsync(incoming);
                continue;
            }

            await _subscriptionMigrator.DisableSubscriptionAsync(incoming);
        }
    }

    private async Task<HashSet<string>> GetArcadePackagesToExclude(string branch, string repoUri, Subscription arcadeSubscription)
    {
        var excludedAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            DependencyFileManager.ArcadeSdkPackageName
        };

        var repo = _gitRepoFactory.CreateClient(repoUri);
        var versionDetailsContents = await repo.GetFileContentsAsync(VersionFiles.VersionDetailsXml, repoUri, branch);
        var versionDetails = _versionDetailsParser.ParseVersionDetailsXml(versionDetailsContents);

        foreach (var dep in versionDetails.Dependencies.Where(d => d.RepoUri.Equals(Constants.ArcadeRepoUri, StringComparison.InvariantCultureIgnoreCase)))
        {
            excludedAssets.Add(dep.Name);
        }

        _logger.LogInformation("Arcade subscription is for {channel} channel. Excluding {count} Arcade assets in backflow subscription",
            arcadeSubscription.Channel.Name,
            excludedAssets.Count);

        return excludedAssets;
    }

    private static bool IsVmrRepoWithOwnOfficialBuild(string repoUri)
        => ReposWithOwnOfficialBuild.Contains(repoUri, StringComparer.OrdinalIgnoreCase);
}
