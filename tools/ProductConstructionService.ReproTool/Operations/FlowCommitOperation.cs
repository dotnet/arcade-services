// Licensed to the .NET Foundation under one or more agreements.
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
    private readonly IProductConstructionServiceApi _localPcsApi;
    private readonly IBarApiClient _barApiClient;

    public FlowCommitOperation(
            FlowCommitOptions options,
            ILogger<FlowCommitOperation> logger,
            GitHubClient ghClient,
            [FromKeyedServices("local")] IProductConstructionServiceApi localPcsApi,
            IBarApiClient barApiClient)
        : base(logger, ghClient, localPcsApi)
    {
        _options = options;
        _logger = logger;
        _ghClient = ghClient;
        _localPcsApi = localPcsApi;
        _barApiClient = barApiClient;
    }

    internal override async Task RunAsync()
    {
        if (_options.Packages.Any() && _options.RealBuildId > 0)
        {
            throw new ArgumentException("Cannot specify both --packages and --realBuildId options.");
        }

        _logger.LogInformation("Flowing commit from {sourceRepo}@{sourceBranch} to {targetRepo}@{targetBranch}",
            _options.SourceRepository,
            _options.SourceBranch,
            _options.TargetRepository,
            _options.TargetBranch);

        var (sourceRepo, sourceOwner) = GitRepoUrlUtils.GetRepoNameAndOwner(_options.SourceRepository);
        var (targetRepo, targetOwner) = GitRepoUrlUtils.GetRepoNameAndOwner(_options.TargetRepository);

        bool isForwardFlow = await IsForwardFlow(sourceOwner, sourceRepo, targetOwner, targetRepo);

        var channelName = $"flow-commit-{Guid.NewGuid()}";
        var namespaceName = channelName;
        var (_, subscription) = await CreateChannelAndSubscriptionAsync(
            @namespace: namespaceName,
            channelName: channelName,
            sourceRepository: _options.SourceRepository,
            targetRepository: _options.TargetRepository,
            targetBranch: _options.TargetBranch,
            sourceEnabled: true,
            sourceDirectory: isForwardFlow ? null : targetRepo,
            targetDirectory: isForwardFlow ? sourceRepo : null);

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

        _logger.LogInformation("Source commit is {}", sourceCommit);
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

        var channel = (await _localPcsApi.Channels.ListChannelsAsync()).First(c => c.Name == channelName);
        await _localPcsApi.Channels.AddBuildToChannelAsync(build.Id, channel.Id);

        _logger.LogInformation("Build created: {buildId}", build.Id);

        await TriggerSubscriptionAsync(subscription.Id, build.Id);

        _logger.LogInformation("Subscription triggered. Wait for a PR in {url}", $"{_options.TargetRepository}/pulls");

        if (_options.SkipCleanup)
        {
            _logger.LogInformation("Skipping cleanup. If you want to re-trigger the subscription run \"darc trigger-subscriptions --ids {subscriptionId} --bar-uri {barUri}\"",
                subscription.Id,
                ProductConstructionServiceApiOptions.PcsLocalUri);
            return;
        }

        _logger.LogInformation("Press enter to finish and cleanup");
        Console.ReadLine();

        await DeleteNamespace(namespaceName);
    }

    private async Task<bool> IsVmr(string repoName, string repoOwner)
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

    private async Task<bool> IsForwardFlow(
        string sourceOwner,
        string sourceRepo,
        string targetOwner,
        string targetRepo)
    {
        if (await IsVmr(targetRepo, targetOwner))
        {
            return true;
        }

        if (await IsVmr(sourceRepo, sourceOwner))
        {
            return false;
        }

        throw new InvalidOperationException("Neither the source nor the target repository appears to be a VMR.");
    }
}
