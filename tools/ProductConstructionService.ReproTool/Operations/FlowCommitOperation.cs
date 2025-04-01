﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib.Helpers;
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

    public FlowCommitOperation(
            FlowCommitOptions options,
            ILogger<FlowCommitOperation> logger,
            GitHubClient ghClient,
            DarcProcessManager darc,
            [FromKeyedServices("local")] IProductConstructionServiceApi localPcsApi)
        : base(logger, ghClient, localPcsApi)
    {
        _options = options;
        _logger = logger;
        _ghClient = ghClient;
        _darc = darc;
        _localPcsApi = localPcsApi;
    }

    internal override async Task RunAsync()
    {
        await _darc.InitializeAsync();

        _logger.LogInformation("Flowing commit from {sourceRepo}@{sourceBranch} to {targetRepo}@{targetBranch}",
            _options.SourceRepository,
            _options.SourceBranch,
            _options.TargetRepository,
            _options.TargetBranch);

        List<Channel> channels = await _localPcsApi.Channels.ListChannelsAsync();
        Channel? channel = channels.FirstOrDefault(c => c.Name == _options.Channel)
            ?? await _localPcsApi.Channels.CreateChannelAsync("test", _options.Channel);

        var subscriptions = await _localPcsApi.Subscriptions.ListSubscriptionsAsync(
            channelId: channel.Id,
            sourceRepository: _options.SourceRepository,
            targetRepository: _options.TargetRepository,
            sourceEnabled: true);

        var (repoName, owner) = GitRepoUrlParser.GetRepoNameAndOwner(_options.SourceRepository);

        var isBackflow = false;
        try
        {
            await _ghClient.Repository.Content.GetAllContents(owner, repoName, SourceMappingsPath);
            isBackflow = true;
        }
        catch { }

        var mappingName = (isBackflow ? _options.TargetRepository : _options.SourceRepository).TrimEnd('/').Split('/').Last();

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
                    SourceEnabled = true,
                    SourceDirectory = isBackflow ? mappingName : null,
                    TargetDirectory = isBackflow ? null : mappingName,
                });

        var commit = (await _ghClient.Repository.Branch.Get(owner, repoName, _options.SourceBranch)).Commit;

        _logger.LogInformation("Creating build for {repo}@{branch} (commit {commit})",
            _options.SourceRepository,
            _options.SourceBranch,
            Microsoft.DotNet.DarcLib.Commit.GetShortSha(commit.Sha));

        var build = await _localPcsApi.Builds.CreateAsync(new BuildData(
            commit.Sha,
            "dnceng",
            "internal",
            $"{DateTime.UtcNow:yyyyMMdd}.{new Random().Next(1, 75)}",
            $"https://dev.azure.com/dnceng/internal/_git/{owner}-{repoName}",
            _options.SourceBranch,
            released: false,
            stable: false)
        {
            GitHubRepository = _options.SourceRepository,
            GitHubBranch = _options.SourceBranch,
        });

        await using var _ = await _darc.AddBuildToChannelAsync(build.Id, channel.Name, skipCleanup: true);

        _logger.LogInformation("Build created: {buildId}", build.Id);

        await TriggerSubscriptionAsync(subscription.Id.ToString(), build.Id);

        _logger.LogInformation("Subscription triggered. Wait for a PR in {url}", $"{_options.TargetRepository}/pulls");
    }
}
