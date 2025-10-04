﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Common;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ProductConstructionService.ReproTool.Options;
using GitHubClient = Octokit.GitHubClient;

namespace ProductConstructionService.ReproTool.Operations;

internal class FlowCommitOperation : Operation
{
    private readonly FlowCommitOptions _options;
    private readonly ILogger<FlowCommitOperation> _logger;
    private readonly GitHubClient _ghClient;
    private readonly DarcProcessManager _darc;
    private readonly IProductConstructionServiceApi _localPcsApi;
    private readonly IBarApiClient _barApiClient;

    public FlowCommitOperation(
            FlowCommitOptions options,
            ILogger<FlowCommitOperation> logger,
            GitHubClient ghClient,
            DarcProcessManager darc,
            [FromKeyedServices("local")] IProductConstructionServiceApi localPcsApi,
            IBarApiClient barApiClient)
        : base(logger, ghClient, localPcsApi)
    {
        _options = options;
        _logger = logger;
        _ghClient = ghClient;
        _darc = darc;
        _localPcsApi = localPcsApi;
        _barApiClient = barApiClient;
    }

    internal override async Task RunAsync()
    {
        if (_options.Packages.Count() > 0 && _options.RealBuildId > 0)
        {
            throw new ArgumentException("Cannot specify both --packages and --realBuildId options.");
        }

        await _darc.InitializeAsync();

        _logger.LogInformation("Flowing commit from {sourceRepo}@{sourceBranch} to {targetRepo}@{targetBranch}",
            _options.SourceRepository,
            _options.SourceBranch,
            _options.TargetRepository,
            _options.TargetBranch);

        List<Channel> channels = await _localPcsApi.Channels.ListChannelsAsync();
        Channel? channel = channels.FirstOrDefault(c => c.Name == _options.Channel)
            ?? await _localPcsApi.Channels.CreateChannelAsync("test", _options.Channel);

        var (sourceRepo, sourceOwner) = GitRepoUrlUtils.GetRepoNameAndOwner(_options.SourceRepository);
        var (targetRepo, targetOwner) = GitRepoUrlUtils.GetRepoNameAndOwner(_options.TargetRepository);

        var (isSourceEnabled, isForwardFlow) = await GetCodeflowMetadata(_options.SourceRepository, _options.TargetRepository);

        var subscriptions = await _localPcsApi.Subscriptions.ListSubscriptionsAsync(
            channelId: channel.Id,
            sourceRepository: _options.SourceRepository,
            targetRepository: _options.TargetRepository,
            sourceEnabled: isSourceEnabled);

        Subscription subscription = subscriptions.FirstOrDefault(s => s.TargetBranch == _options.TargetBranch)
            ?? await _localPcsApi.Subscriptions.CreateAsync(
                new SubscriptionData(
                    channelName: channel.Name,
                    sourceRepository: _options.SourceRepository,
                    targetRepository: _options.TargetRepository,
                    targetBranch: _options.TargetBranch,
                    new SubscriptionPolicy(batchable: false, UpdateFrequency.None)
                    {
                        MergePolicies = [new MergePolicy() { Name = "Standard" }]
                    },
                    null)
                {
                    SourceEnabled = isSourceEnabled,
                    SourceDirectory = isForwardFlow ? null : targetRepo,
                    TargetDirectory = isForwardFlow ? sourceRepo : null,
                });

        string sourceCommit;

        if (string.IsNullOrEmpty(_options.SourceCommit) && string.IsNullOrEmpty(_options.SourceBranch))
        {
            throw new ArgumentException("Please provide a source-branch or source-commit value.");
        }

        if (string.IsNullOrEmpty(_options.SourceCommit))
        {
            sourceCommit = (await _ghClient.Repository.Branch.Get(sourceOwner, sourceRepo, _options.SourceBranch))
                .Commit.Sha;
        }
        else
        {
            sourceCommit = _options.SourceCommit;
        }

        _logger.LogInformation("Creating build for {repo}@{branch} (commit {commit})",
            _options.SourceRepository,
            _options.SourceBranch,
            Microsoft.DotNet.DarcLib.Commit.GetShortSha(sourceCommit));

        List<AssetData> assets;
        if (_options.RealBuildId > 0)
        {
            assets = CreateAssetDataFromBuild(await _barApiClient.GetBuildAsync(_options.RealBuildId));
        }
        else
        {
            assets = [
                .._options.Packages.Select(p => new AssetData(true)
                {
                    Name = p,
                    Version = $"1.0.0-{Guid.NewGuid().ToString().Substring(0, 8)}",
                })
            ];
        }

        _logger.LogInformation("source commit is {}", sourceCommit);
        _logger.LogInformation("Subscription is source enabled: {}", isSourceEnabled);
        _logger.LogInformation("Subscription is forward-flow: {}", isForwardFlow);

        var build = await _localPcsApi.Builds.CreateAsync(new BuildData(
                sourceCommit,
                "dnceng",
                "internal",
                $"{DateTime.UtcNow:yyyyMMdd}.{new Random().Next(1, 75)}",
                $"https://dev.azure.com/dnceng/internal/_git/{sourceOwner}-{sourceRepo}",
                _options.SourceBranch ?? sourceCommit,
                released: false,
                stable: false)
            {
                GitHubRepository = _options.SourceRepository,
                GitHubBranch = _options.SourceBranch,
                Assets = assets
            });

        await using var _ = await _darc.AddBuildToChannelAsync(build.Id, channel.Name, skipCleanup: true);

        _logger.LogInformation("Build created: {buildId}", build.Id);

        await TriggerSubscriptionAsync(subscription.Id.ToString(), build.Id);

        _logger.LogInformation("Subscription triggered. Wait for a PR in {url}", $"{_options.TargetRepository}/pulls");
    }

    private async Task<bool> IsRepoVmr(string repoName, string repoOwner)
    {
        try
        {
            await _ghClient.Repository.Content.GetAllContents(repoOwner, repoName, SourceManifestPath);
            return true;
        }
        catch (Octokit.ApiException e)
        {
            if (e.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return false;
            }
            throw;
        }
    }

    private async Task<(bool isSourceEnabled, bool isForwardFlow)> GetCodeflowMetadata(
        string sourceUri,
        string targetUri)
    {
        var (targetRepo, targetOwner) = GitRepoUrlUtils.GetRepoNameAndOwner(targetUri);
        if (await IsRepoVmr(targetRepo, targetOwner))
        {
            return (true, true);
        }

        var (sourceRepo, sourceOwner) = GitRepoUrlUtils.GetRepoNameAndOwner(sourceUri);
        if (await IsRepoVmr(sourceRepo, sourceOwner))
        {
            return (true, false);
        }

        return (false, false);
    }
}
