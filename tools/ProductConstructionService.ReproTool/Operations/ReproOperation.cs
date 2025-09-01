// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Common;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Octokit;
using ProductConstructionService.ReproTool.Options;
using Build = Microsoft.DotNet.ProductConstructionService.Client.Models.Build;
using GitHubClient = Octokit.GitHubClient;
using Subscription = Microsoft.DotNet.ProductConstructionService.Client.Models.Subscription;

namespace ProductConstructionService.ReproTool.Operations;

internal class ReproOperation(
    IBarApiClient prodBarClient,
    ReproOptions options,
    DarcProcessManager darcProcessManager,
    [FromKeyedServices("local")] IProductConstructionServiceApi localPcsApi,
    GitHubClient ghClient,
    ILogger<ReproOperation> logger) : Operation(logger, ghClient, localPcsApi)
{
    internal override async Task RunAsync()
    {
        logger.LogInformation("Fetching {subscriptionId} subscription from BAR",
            options.Subscription);
        var subscription = await prodBarClient.GetSubscriptionAsync(options.Subscription);

        if (subscription == null)
        {
            throw new ArgumentException($"Couldn't find subscription with subscription id {options.Subscription}");
        }

        await darcProcessManager.InitializeAsync();
        if (subscription.SourceEnabled)
        {
            await ReproCodeFlowSubscription(subscription);
        }
        else
        {
             await ReproDependencyFlowSubscription(subscription);
        }

    }

    private async Task ReproCodeFlowSubscription(Subscription subscription)
    {
        if (!string.IsNullOrEmpty(subscription.SourceDirectory) && !string.IsNullOrEmpty(subscription.TargetDirectory))
        {
            throw new ArgumentException("Code flow subscription incorrectly configured: is missing SourceDirectory or TargetDirectory");
        }

        if (!string.IsNullOrEmpty(options.Commit) && options.BuildId != null)
        {
            throw new ArgumentException($"Only one of {nameof(ReproOptions.Commit)} and {nameof(ReproOptions.BuildId)} can be provided");
        }

        Build? build = null;
        if (options.BuildId != null)
        {
            build = await prodBarClient.GetBuildAsync(options.BuildId.Value);
            if (build.GitHubRepository != subscription.SourceRepository)
            {
                throw new ArgumentException($"Build {build.Id} repository {build.GitHubRepository} doesn't match the subscription source repository {subscription.SourceRepository}");
            }
        }

        var defaultChannel = (await prodBarClient.GetDefaultChannelsAsync(repository: subscription.SourceRepository, channel: subscription.Channel.Name)).First();

        string vmrBranch, productRepoUri, productRepoBranch;
        var isForwardFlow = !string.IsNullOrEmpty(subscription.TargetDirectory);
        if (isForwardFlow)
        {
            vmrBranch = subscription.TargetBranch;
            productRepoUri = subscription.SourceRepository;
            productRepoBranch = defaultChannel.Branch;
        }
        else
        {
            vmrBranch = defaultChannel.Branch;
            productRepoUri = subscription.TargetRepository;
            productRepoBranch = subscription.TargetBranch;
        }
        var productRepoForkUri = ProductRepoFormat + productRepoUri.Split('/', StringSplitOptions.RemoveEmptyEntries).Last();
        logger.LogInformation("Reproducing subscription from {sourceRepo} to {targetRepo}",
            isForwardFlow ? productRepoForkUri : VmrForkUri,
            isForwardFlow ? VmrForkUri : productRepoForkUri);

        await using var vmrTmpBranch = await PrepareVmrForkAsync(vmrBranch, options.SkipCleanup);
        await UpdateVmrSourceFiles(vmrTmpBranch.Value, productRepoUri, productRepoForkUri);

        logger.LogInformation("Preparing product repo fork {productRepoFork}, branch {branch}", productRepoForkUri, productRepoBranch);
        await using var productRepoTmpBranch = await PrepareProductRepoForkAsync(productRepoUri, productRepoForkUri, productRepoBranch, options.SkipCleanup);

        // Find the latest commit in the source repo to create a build from
        string sourceRepoSha;
        (var sourceRepoName, var sourceRepoOwner) = GitRepoUrlUtils.GetRepoNameAndOwner(subscription.SourceRepository);
        if (build != null)
        {
            sourceRepoSha = build.Commit;
        }
        else if (string.IsNullOrEmpty(options.Commit))
        {
            sourceRepoSha = await GetLatestCommitInBranch(sourceRepoOwner, sourceRepoName, defaultChannel.Branch);
        }
        else
        {
            // Validate that the commit actually exists
            try
            {
                await GetCommit(sourceRepoOwner, sourceRepoName, options.Commit);
            }
            catch (NotFoundException)
            {
                throw new ArgumentException($"Commit {options.Commit} doesn't exist in repo {subscription.SourceRepository}");
            }
            sourceRepoSha = options.Commit;
        }

        var channelName = $"repro-{Guid.NewGuid()}";
        await using var channel = await darcProcessManager.CreateTestChannelAsync(channelName, options.SkipCleanup);

        var testBuild = await CreateBuildAsync(
            isForwardFlow ? productRepoForkUri : VmrForkUri,
            isForwardFlow ? productRepoTmpBranch.Value : vmrTmpBranch.Value,
            sourceRepoSha,
            build != null ? CreateAssetDataFromBuild(build) : []);

        await using var testSubscription = await darcProcessManager.CreateSubscriptionAsync(
            channel: channelName,
            sourceRepo: isForwardFlow ? productRepoForkUri : VmrForkUri,
            targetRepo: isForwardFlow ? VmrForkUri : productRepoForkUri,
            targetBranch: isForwardFlow ? vmrTmpBranch.Value : productRepoTmpBranch.Value,
            sourceEnabled: true,
            sourceDirectory: subscription.SourceDirectory,
            targetDirectory: subscription.TargetDirectory,
            skipCleanup: options.SkipCleanup);

        await darcProcessManager.AddBuildToChannelAsync(testBuild.Id, channelName, options.SkipCleanup);

        await TriggerSubscriptionAsync(testSubscription.Value);

        if (options.SkipCleanup)
        {
            logger.LogInformation("Skipping cleanup. If you want to re-trigger the reproduced subscription run \"darc trigger-subscriptions --ids {subscriptionId} --bar-uri {barUri}\"",
                testSubscription.Value,
                ProductConstructionServiceApiOptions.PcsLocalUri);
            return;
        }

        logger.LogInformation("Code flow successfully recreated. Press enter to finish and cleanup");
        Console.ReadLine();

        // Cleanup
        if (isForwardFlow)
        {
            await DeleteDarcPRBranchAsync(VmrForkRepoName, vmrTmpBranch.Value);
        }
        else
        {
            await DeleteDarcPRBranchAsync(productRepoUri.Split('/').Last(), productRepoTmpBranch.Value);
        }
    }

    private async Task ReproDependencyFlowSubscription(Subscription subscription)
    {

        if (options.BuildId == null)
        {
             throw new ArgumentException("A buildId must be provided to flow a dependency update subscription");
        }
        var build = await prodBarClient.GetBuildAsync(options.BuildId.Value);

        if (build.GitHubRepository != subscription.SourceRepository)
        {
            throw new ArgumentException($"Build {build.Id} repository {build.GitHubRepository} doesn't match the subscription source repository {subscription.SourceRepository}");
        }

        var targetRepoFork = ProductRepoFormat + subscription.TargetRepository.Split('/', StringSplitOptions.RemoveEmptyEntries).Last();
        var sourceRepoFork = ProductRepoFormat + subscription.SourceRepository.Split('/', StringSplitOptions.RemoveEmptyEntries).Last();
        var targetBranch = subscription.TargetBranch;
        await using var targetRepoTmpBranch = await PrepareProductRepoForkAsync(subscription.TargetRepository, targetRepoFork, targetBranch, options.SkipCleanup);

        var channelName = $"repro-{Guid.NewGuid()}";
        await using var channel = await darcProcessManager.CreateTestChannelAsync(channelName, options.SkipCleanup);

        var testBuild = await CreateBuildAsync(
            sourceRepoFork,
            "branch",
            "sha",
            CreateAssetDataFromBuild(build));

        await using var testSubscription = await darcProcessManager.CreateSubscriptionAsync(
            sourceRepoFork,
            targetRepoFork,
            channelName,
            targetRepoTmpBranch.Value,
            sourceEnabled: false,
            sourceDirectory: null,
            targetDirectory: null,
            skipCleanup: options.SkipCleanup);

        await darcProcessManager.AddBuildToChannelAsync(testBuild.Id, channelName, options.SkipCleanup);

        await TriggerSubscriptionAsync(testSubscription.Value);

        logger.LogInformation("Code flow successfully recreated. Press enter to finish and cleanup");
        Console.ReadLine();
        await DeleteDarcPRBranchAsync(targetRepoFork.Split('/').Last(), targetRepoTmpBranch.Value);
    }
}
