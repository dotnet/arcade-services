// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tools.Common;
using GitHubClient = Octokit.GitHubClient;

namespace ProductConstructionService.ReproTool.Operations;

internal class ForwardFlowTestOperation(
    VmrDependencyResolver vmrDependencyResolver,
    ILogger<ForwardFlowTestOperation> logger,
    GitHubClient ghClient,
    DarcProcessManager darcProcessManager,
    IBarApiClient prodBarClient,
    [FromKeyedServices("local")] IProductConstructionServiceApi localPcsApi) : Operation(logger, ghClient, localPcsApi)
{
    internal override async Task RunAsync()
    {
        await darcProcessManager.InitializeAsync();

        var vmrRepos = await vmrDependencyResolver.GetVmrRepositoriesAsync(
            "https://github.com/dotnet/dotnet",
            "https://github.com/dotnet/sdk",
            "main");

        var vmrTestBranch = await PrepareVmrForkAsync("main", skipCleanup: true);

        var channelName = $"repro-{Guid.NewGuid()}";
        await using var channel = await darcProcessManager.CreateTestChannelAsync(channelName, true);

        foreach (var vmrRepo in vmrRepos)
        {
            var productRepoForkUri = $"{ProductRepoFormat}{vmrRepo.Mapping.DefaultRemote.Split('/', StringSplitOptions.RemoveEmptyEntries).Last()}";
            var latestBuild = await prodBarClient.GetLatestBuildAsync(vmrRepo.Mapping.DefaultRemote, vmrRepo.Channel.Channel.Id);

            var productRepoTmpBranch = await PrepareProductRepoForkAsync(vmrRepo.Mapping.DefaultRemote, productRepoForkUri, latestBuild.GetBranch(), false);

            var testBuild = await CreateBuildAsync(
                productRepoForkUri,
                productRepoTmpBranch.Value,
                latestBuild.Commit,
                []);

            await UpdateVmrSourceFiles(
                vmrTestBranch.Value,
                vmrRepo.Mapping.DefaultRemote,
                productRepoForkUri);

            await using var testSubscription = await darcProcessManager.CreateSubscriptionAsync(
                channel: channelName,
                sourceRepo: productRepoForkUri,
                targetRepo: VmrForkUri,
                targetBranch: vmrTestBranch.Value,
                sourceDirectory: null,
                targetDirectory: vmrRepo.Mapping.Name,
                skipCleanup: true);

            await darcProcessManager.AddBuildToChannelAsync(testBuild.Id, channelName, skipCleanup: true);

            await TriggerSubscriptionAsync(testSubscription.Value);
        }
    }
}
